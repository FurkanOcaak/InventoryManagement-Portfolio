import { apiFetch } from '../api/apiFetch'
import { useEffect, useRef, useState } from 'react'
import { Users } from 'lucide-react'
import { Card, EmptyState, PageHeading } from '../components/ui/PageElements'
import { CustomerCreateForm } from '../components/customers/CustomerCreateForm'
import { CustomerEditForm } from '../components/customers/CustomerEditForm'

interface Customer {
  id: string
  code: string
  name: string
  email: string | null
  phone: string | null
  taxNumber: string | null
  address: string | null
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string | null
}

type CustomersState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'success'; customers: Customer[] }

type CustomerTab = 'active' | 'inactive'

const dateFormatter = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeZone: 'Europe/Istanbul' })

function displayText(value: string | null) {
  return value?.trim() || '—'
}

function formatCreatedAt(value: string) {
  const text = value.trim()
  if (!text) return '—'
  const date = new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(text) ? text : `${text}Z`)
  return Number.isNaN(date.getTime()) ? '—' : dateFormatter.format(date)
}

function isCustomer(value: unknown): value is Customer {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const item = value as Record<string, unknown>
  return ['id', 'code', 'name', 'createdAtUtc'].every((field) => typeof item[field] === 'string')
    && ['email', 'phone', 'taxNumber', 'address', 'updatedAtUtc'].every((field) => item[field] === null || typeof item[field] === 'string')
    && typeof item.isActive === 'boolean'
}

export function CustomersPage() {
  const [state, setState] = useState<CustomersState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)
  const [activeTab, setActiveTab] = useState<CustomerTab>('active')
  const listRequestRef = useRef<AbortController | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [editingCustomer, setEditingCustomer] = useState<Customer | null>(null)
  const [successMessage, setSuccessMessage] = useState('')
  const [deactivationError, setDeactivationError] = useState('')
  const [deactivatingId, setDeactivatingId] = useState<string | null>(null)
  const deactivationPendingRef = useRef(false)
  const [reactivationError, setReactivationError] = useState('')
  const [reactivatingId, setReactivatingId] = useState<string | null>(null)
  const reactivationPendingRef = useRef(false)
  const mountedRef = useRef(false)
  const openButtonRef = useRef<HTMLButtonElement>(null)
  const wasFormOpenRef = useRef(false)
  const editButtonRef = useRef<HTMLButtonElement | null>(null)
  const actionBusy = deactivatingId !== null || reactivatingId !== null
  const tabsLocked = formOpen || editingCustomer !== null || actionBusy

  useEffect(() => {
    mountedRef.current = true
    const preventLeaving = (event: BeforeUnloadEvent) => {
      if (deactivationPendingRef.current || reactivationPendingRef.current) { event.preventDefault(); event.returnValue = '' }
    }
    const preventNavigation = (event: MouseEvent) => {
      if ((deactivationPendingRef.current || reactivationPendingRef.current) && event.target instanceof Element && event.target.closest('a[href]')) {
        event.preventDefault()
        event.stopImmediatePropagation()
      }
    }
    window.addEventListener('beforeunload', preventLeaving)
    document.addEventListener('click', preventNavigation, true)
    return () => {
      mountedRef.current = false
      window.removeEventListener('beforeunload', preventLeaving)
      document.removeEventListener('click', preventNavigation, true)
      // A client-side cancellation cannot undo a server-side customer update.
    }
  }, [])

  useEffect(() => {
    if (!editingCustomer && editButtonRef.current) {
      const target = editButtonRef.current.isConnected ? editButtonRef.current : openButtonRef.current
      target?.focus()
      editButtonRef.current = null
    }
  }, [editingCustomer])

  useEffect(() => {
    if (!formOpen && wasFormOpenRef.current) openButtonRef.current?.focus()
    wasFormOpenRef.current = formOpen
  }, [formOpen])

  useEffect(() => {
    const controller = new AbortController()
    listRequestRef.current = controller
    async function loadCustomers() {
      try {
        const response = await apiFetch(activeTab === 'active' ? '/api/customers' : '/api/customers/inactive', { signal: controller.signal })
        if (!response.ok) throw new Error(`Müşteriler yüklenemedi (HTTP ${response.status}). Lütfen tekrar deneyin.`)
        const customers: unknown = await response.json()
        if (!Array.isArray(customers) || !customers.every(isCustomer)) {
          throw new Error('Sunucudan geçerli bir müşteri listesi alınamadı. Lütfen tekrar deneyin.')
        }
        if (!controller.signal.aborted) setState({ status: 'success', customers })
      } catch (error: unknown) {
        if (controller.signal.aborted) return
        const message = error instanceof TypeError
          ? 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.'
          : error instanceof SyntaxError
            ? 'Sunucudan geçerli bir müşteri yanıtı alınamadı. Lütfen tekrar deneyin.'
            : error instanceof Error
              ? error.message
              : 'Müşteriler yüklenirken beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.'
        setState({ status: 'error', message })
      }
    }
    void loadCustomers()
    return () => controller.abort()
  }, [attempt, activeTab])

  function retry() {
    listRequestRef.current?.abort()
    setState({ status: 'loading' })
    setAttempt((previous) => previous + 1)
  }

  function selectTab(tab: CustomerTab) {
    if (tab === activeTab || tabsLocked || deactivationPendingRef.current || reactivationPendingRef.current) return
    setSuccessMessage('')
    setDeactivationError('')
    setReactivationError('')
    setActiveTab(tab)
    retry()
  }

  async function reactivate(customer: Customer) {
    if (reactivationPendingRef.current || deactivationPendingRef.current || formOpen || editingCustomer || activeTab !== 'inactive' || customer.isActive) return
    if (!window.confirm(`${customer.code} kodlu ${customer.name} adlı müşteriyi yeniden aktifleştirmek istediğinize emin misiniz? Müşteri aktif listeye geri dönecek.`)) return

    reactivationPendingRef.current = true
    setReactivatingId(customer.id)
    setSuccessMessage('')
    setReactivationError('')
    const uncertainMessage = 'Müşterinin aktifleştirilip aktifleştirilmediği doğrulanamadı. Tekrar denemeden önce müşteri listelerini kontrol edin.'
    try {
      const response = await apiFetch(`/api/customers/${encodeURIComponent(customer.id)}/reactivate`, { method: 'PATCH' })
      if (!mountedRef.current) return
      if (response.status === 200) {
        setSuccessMessage('Müşteri başarıyla aktifleştirildi.')
        retry()
      } else if (response.status === 404) {
        setReactivationError('Müşteri artık bulunamıyor.')
        retry()
      } else if (response.status === 409) {
        setReactivationError('Müşteri zaten aktif.')
        retry()
      } else {
        setReactivationError(`${uncertainMessage} (HTTP ${response.status})`)
      }
    } catch {
      if (mountedRef.current) setReactivationError(uncertainMessage)
    } finally {
      reactivationPendingRef.current = false
      if (mountedRef.current) setReactivatingId(null)
    }
  }

  async function deactivate(customer: Customer) {
    if (deactivationPendingRef.current || reactivationPendingRef.current || formOpen || editingCustomer || activeTab !== 'active' || !customer.isActive) return
    if (!window.confirm(`${customer.code} kodlu ${customer.name} adlı müşteriyi pasifleştirmek istediğinize emin misiniz? Müşteri aktif listeden kaldırılacak.`)) return

    deactivationPendingRef.current = true
    setDeactivatingId(customer.id)
    setSuccessMessage('')
    setDeactivationError('')
    const uncertainMessage = 'Müşterinin pasifleştirilip pasifleştirilmediği doğrulanamadı. Tekrar denemeden önce müşteri listesini kontrol edin.'
    try {
      const response = await apiFetch(`/api/customers/${encodeURIComponent(customer.id)}`, { method: 'DELETE' })
      if (!mountedRef.current) return
      if (response.status === 204) {
        setSuccessMessage('Müşteri başarıyla pasifleştirildi.')
        retry()
      } else if (response.status === 404) {
        setDeactivationError('Müşteri artık bulunamıyor.')
        retry()
      } else {
        setDeactivationError(`${uncertainMessage} (HTTP ${response.status})`)
      }
    } catch {
      if (mountedRef.current) setDeactivationError(uncertainMessage)
    } finally {
      deactivationPendingRef.current = false
      if (mountedRef.current) setDeactivatingId(null)
    }
  }

  return (
    <>
      <PageHeading eyebrow="İŞ ORTAKLARI" title="Müşteriler" description="Müşteri kayıtlarınızı tek bir yerden takip edin." />
      <div className="customers-toolbar">
        <div className="customer-tabs" role="tablist" aria-label="Müşteri durumu">
          {(['active', 'inactive'] as const).map((tab) => <button key={tab} id={`customers-tab-${tab}`} type="button" role="tab" aria-selected={activeTab === tab} aria-controls="customers-panel" tabIndex={activeTab === tab ? 0 : -1} disabled={tabsLocked} onClick={() => selectTab(tab)} onKeyDown={(event) => {
            if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
              event.preventDefault()
              const target = event.key === 'Home' ? 'active' : event.key === 'End' ? 'inactive' : tab === 'active' ? 'inactive' : 'active'
              document.getElementById(`customers-tab-${target}`)?.focus()
            }
          }}>{tab === 'active' ? 'Aktif Müşteriler' : 'Pasif Müşteriler'}</button>)}
        </div>
        {activeTab === 'active' && <button ref={openButtonRef} type="button" className="customers-retry" disabled={tabsLocked} aria-expanded={formOpen} aria-controls={formOpen ? 'customer-create-form' : undefined} onClick={() => { setSuccessMessage(''); setDeactivationError(''); setFormOpen(true) }}>Yeni Müşteri Ekle</button>}
      </div>
      {successMessage && <p className="product-create-success" role="status">{successMessage}</p>}
      {(deactivationError || reactivationError) && <div className="customer-deactivation-error"><p className="product-form-error" role="alert">{deactivationError || reactivationError}</p><button type="button" className="product-cancel" onClick={retry} disabled={actionBusy}>Müşteri Listesini Yenile</button></div>}
      {deactivatingId !== null && <p className="customer-deactivation-progress" role="status">Müşteri pasifleştiriliyor...</p>}
      {reactivatingId !== null && <p className="customer-deactivation-progress" role="status">Müşteri aktifleştiriliyor...</p>}
      {formOpen && <CustomerCreateForm onCancel={() => setFormOpen(false)} onSuccess={() => { setFormOpen(false); setSuccessMessage('Müşteri başarıyla eklendi.'); retry() }} />}
      {editingCustomer && <CustomerEditForm key={editingCustomer.id} customer={editingCustomer} onCancel={() => setEditingCustomer(null)} onRefresh={retry} onSuccess={() => { setEditingCustomer(null); setSuccessMessage('Müşteri başarıyla güncellendi.'); retry() }} />}
      <div id="customers-panel" role="tabpanel" aria-labelledby={`customers-tab-${activeTab}`} tabIndex={0}>
      <Card title={activeTab === 'active' ? 'Müşteri listesi' : 'Pasif müşteri listesi'} icon={Users}>
        {state.status === 'loading' && <div className="customers-state" role="status">{activeTab === 'active' ? 'Müşteriler yükleniyor...' : 'Pasif müşteriler yükleniyor...'}</div>}
        {state.status === 'error' && <div className="customers-state"><p role="alert">{state.message}</p><button type="button" className="customers-retry" onClick={retry}>Tekrar Dene</button></div>}
        {state.status === 'success' && (state.customers.length === 0 ? (
          <EmptyState icon={Users} title={activeTab === 'active' ? 'Henüz müşteri eklenmemiş.' : 'Pasifleştirilmiş müşteri bulunmuyor.'} description={activeTab === 'active' ? 'Müşteri kayıtları eklendiğinde bu alanda listelenecek.' : 'Pasifleştirilen müşteriler bu alanda listelenecek.'} />
        ) : (
          <div className="customers-table-scroll" role="region" aria-label={activeTab === 'active' ? 'Aktif müşteri listesi' : 'Pasif müşteri listesi'} tabIndex={0}>
            <table className="customers-table">
              <caption>{activeTab === 'active' ? 'Aktif müşteriler' : 'Pasif müşteriler'} — {state.customers.length} kayıt</caption>
              <thead><tr>
                <th scope="col">Müşteri Kodu</th><th scope="col">Müşteri Adı</th><th scope="col">E-posta</th><th scope="col">Telefon</th><th scope="col">Vergi Numarası</th><th scope="col">Adres</th><th scope="col">Durum</th><th scope="col">Oluşturulma Tarihi</th><th scope="col">İşlemler</th>
              </tr></thead>
              <tbody>{state.customers.map((customer) => (
                <tr key={customer.id}>
                  <td>{displayText(customer.code)}</td>
                  <th scope="row">{displayText(customer.name)}</th>
                  <td className="customer-contact">{displayText(customer.email)}</td>
                  <td>{displayText(customer.phone)}</td>
                  <td>{displayText(customer.taxNumber)}</td>
                  <td className="customer-address">{displayText(customer.address)}</td>
                  <td><span className={`customer-status ${customer.isActive ? 'is-active' : 'is-inactive'}`}>{customer.isActive ? 'Aktif' : 'Pasif'}</span></td>
                  <td className="customer-date">{formatCreatedAt(customer.createdAtUtc)}</td>
                  <td><div className="customer-row-actions">
                    {activeTab === 'active' ? <>
                      <button type="button" className="customer-edit-button" disabled={tabsLocked} aria-label={`${customer.code} kodlu ${customer.name} müşterisini düzenle`} onClick={(event) => { editButtonRef.current = event.currentTarget; setSuccessMessage(''); setDeactivationError(''); setEditingCustomer(customer) }}>Düzenle</button>
                      {customer.isActive && <button type="button" className="customer-deactivate-button" disabled={tabsLocked} aria-label={`${customer.code} kodlu ${customer.name} müşterisini pasifleştir`} onClick={() => void deactivate(customer)}>{deactivatingId === customer.id ? 'Pasifleştiriliyor...' : 'Pasifleştir'}</button>}
                    </> : <button type="button" className="customer-reactivate-button" disabled={tabsLocked || customer.isActive} aria-label={`${customer.code} kodlu ${customer.name} müşterisini aktifleştir`} onClick={() => void reactivate(customer)}>{reactivatingId === customer.id ? 'Aktifleştiriliyor...' : 'Aktifleştir'}</button>}
                  </div></td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        ))}
      </Card>
      </div>
    </>
  )
}
