import { roles, useAuth } from '../auth/AuthContext'
import { apiFetch } from '../api/apiFetch'
import { useEffect, useRef, useState } from 'react'
import { Building2 } from 'lucide-react'
import { Card, EmptyState, PageHeading } from '../components/ui/PageElements'
import { WarehouseCreateForm } from '../components/warehouses/WarehouseCreateForm'
import { WarehouseEditForm } from '../components/warehouses/WarehouseEditForm'

interface Warehouse {
  id: string
  code: string
  name: string
  location: string | null
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string | null
}

type WarehousesState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'success'; warehouses: Warehouse[] }

type WarehouseTab = 'active' | 'inactive'

const dateFormatter = new Intl.DateTimeFormat('tr-TR', {
  dateStyle: 'medium',
  timeZone: 'Europe/Istanbul',
})

function formatCreatedAt(value: string) {
  const text = value.trim()
  if (!text) return '—'
  // SQL-backed UTC timestamps can arrive without a timezone suffix.
  const date = new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(text) ? text : `${text}Z`)
  return Number.isNaN(date.getTime()) ? '—' : dateFormatter.format(date)
}

export function WarehousesPage() {
  const { user } = useAuth()
  const canManage = user?.roles.includes(roles.admin) ?? false
  const [state, setState] = useState<WarehousesState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)
  const [activeTab, setActiveTab] = useState<WarehouseTab>('active')
  const listRequestRef = useRef<AbortController | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [editingWarehouse, setEditingWarehouse] = useState<Warehouse | null>(null)
  const [successMessage, setSuccessMessage] = useState('')
  const [deactivationError, setDeactivationError] = useState('')
  const [deactivatingId, setDeactivatingId] = useState<string | null>(null)
  const deactivationRequestRef = useRef<AbortController | null>(null)
  const [reactivationError, setReactivationError] = useState('')
  const [reactivatingId, setReactivatingId] = useState<string | null>(null)
  const reactivationRequestRef = useRef<AbortController | null>(null)
  const createButtonRef = useRef<HTMLButtonElement>(null)
  const formWasOpenRef = useRef(false)
  const editButtonRef = useRef<HTMLButtonElement | null>(null)

  const actionBusy = deactivatingId !== null || reactivatingId !== null
  const tabsLocked = formOpen || editingWarehouse !== null || actionBusy

  useEffect(() => () => {
    deactivationRequestRef.current?.abort()
    reactivationRequestRef.current?.abort()
  }, [])

  useEffect(() => {
    if (!editingWarehouse && editButtonRef.current) {
      const target = editButtonRef.current.isConnected ? editButtonRef.current : createButtonRef.current
      target?.focus()
      editButtonRef.current = null
    }
  }, [editingWarehouse])

  useEffect(() => {
    if (!formOpen && formWasOpenRef.current) createButtonRef.current?.focus()
    formWasOpenRef.current = formOpen
  }, [formOpen])

  useEffect(() => {
    const controller = new AbortController()
    listRequestRef.current = controller

    async function loadWarehouses() {
      try {
        const response = await apiFetch(activeTab === 'active' ? '/api/warehouses' : '/api/warehouses/inactive', { signal: controller.signal })
        if (!response.ok) {
          throw new Error(`Depolar yüklenemedi (HTTP ${response.status}). Lütfen tekrar deneyin.`)
        }
        const warehouses: Warehouse[] = await response.json()
        if (!Array.isArray(warehouses)) {
          throw new Error('Sunucudan beklenen depo listesi alınamadı. Lütfen tekrar deneyin.')
        }
        if (!controller.signal.aborted) setState({ status: 'success', warehouses })
      } catch (error: unknown) {
        if (controller.signal.aborted) return
        const message = error instanceof TypeError
          ? 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.'
          : error instanceof SyntaxError
            ? 'Sunucudan geçerli bir depo yanıtı alınamadı. Lütfen tekrar deneyin.'
            : error instanceof Error
              ? error.message
              : 'Depolar yüklenirken beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.'
        setState({ status: 'error', message })
      }
    }

    void loadWarehouses()
    return () => controller.abort()
  }, [attempt, activeTab])

  function retry() {
    listRequestRef.current?.abort()
    setState({ status: 'loading' })
    setAttempt((previous) => previous + 1)
  }

  function selectTab(tab: WarehouseTab) {
    if (tab === 'inactive' && !canManage) return
    if (tab === activeTab || tabsLocked || deactivationRequestRef.current || reactivationRequestRef.current) return
    setSuccessMessage('')
    setDeactivationError('')
    setReactivationError('')
    setActiveTab(tab)
    retry()
  }

  async function reactivate(warehouse: Warehouse) {
    if (!canManage) return
    if (reactivationRequestRef.current || deactivationRequestRef.current || formOpen || editingWarehouse || activeTab !== 'inactive' || warehouse.isActive) return
    if (!window.confirm(`${warehouse.code} kodlu ${warehouse.name} adlı depoyu yeniden aktifleştirmek istediğinize emin misiniz? Depo aktif listede yeniden görünecek.`)) return

    const controller = new AbortController()
    reactivationRequestRef.current = controller
    setReactivatingId(warehouse.id)
    setSuccessMessage('')
    setReactivationError('')
    try {
      const response = await apiFetch(`/api/warehouses/${encodeURIComponent(warehouse.id)}/reactivate`, {
        method: 'PATCH',
        signal: controller.signal,
      })
      if (controller.signal.aborted) return
      if (response.status === 200) {
        setSuccessMessage('Depo başarıyla aktifleştirildi.')
        retry()
      } else if (response.status === 404) {
        setReactivationError('Depo artık bulunamıyor.')
        retry()
      } else if (response.status === 409) {
        setReactivationError('Depo zaten aktif.')
        retry()
      } else {
        setReactivationError(`Depo aktifleştirilemedi (HTTP ${response.status}). Depo listelerini kontrol ettikten sonra tekrar deneyin.`)
      }
    } catch {
      if (!controller.signal.aborted) {
        setReactivationError('Sunucuya ulaşılamadı veya yanıt alınamadı. Bağlantınızı ve depo listelerini kontrol ettikten sonra tekrar deneyin.')
      }
    } finally {
      if (!controller.signal.aborted) {
        reactivationRequestRef.current = null
        setReactivatingId(null)
      }
    }
  }

  async function deactivate(warehouse: Warehouse) {
    if (!canManage) return
    if (deactivationRequestRef.current || reactivationRequestRef.current || formOpen || editingWarehouse || activeTab !== 'active' || !warehouse.isActive) return
    if (!window.confirm(`${warehouse.code} kodlu ${warehouse.name} adlı depoyu pasifleştirmek istediğinize emin misiniz? Depo aktif listeden kaldırılacak.`)) return

    const controller = new AbortController()
    deactivationRequestRef.current = controller
    setDeactivatingId(warehouse.id)
    setSuccessMessage('')
    setDeactivationError('')
    try {
      const response = await apiFetch(`/api/warehouses/${encodeURIComponent(warehouse.id)}`, {
        method: 'DELETE',
        signal: controller.signal,
      })
      if (controller.signal.aborted) return
      if (response.status === 204) {
        setSuccessMessage('Depo başarıyla pasifleştirildi.')
        retry()
      } else if (response.status === 404) {
        setDeactivationError('Depo artık bulunamıyor.')
        retry()
      } else {
        setDeactivationError(`Depo pasifleştirilemedi (HTTP ${response.status}). Depo listesini kontrol ettikten sonra tekrar deneyin.`)
      }
    } catch {
      if (!controller.signal.aborted) {
        setDeactivationError('Sunucuya ulaşılamadı veya yanıt alınamadı. Bağlantınızı ve depo listesini kontrol ettikten sonra tekrar deneyin.')
      }
    } finally {
      if (!controller.signal.aborted) {
        deactivationRequestRef.current = null
        setDeactivatingId(null)
      }
    }
  }

  return (
    <>
      <PageHeading eyebrow="ENVANTER YÖNETİMİ" title="Depolar" description="Depolarınızı ve envanterinizin dağılımını takip edin." />
      <div className="warehouses-toolbar">
        <div className="warehouse-tabs" role="tablist" aria-label="Depo durumu">
          {(canManage ? ['active', 'inactive'] as const : ['active'] as const).map((tab) => <button key={tab} id={`warehouses-tab-${tab}`} type="button" role="tab" aria-selected={activeTab === tab} aria-controls="warehouses-panel" tabIndex={activeTab === tab ? 0 : -1} disabled={tabsLocked} onClick={() => selectTab(tab)} onKeyDown={(event) => {
            if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
              event.preventDefault()
              const target = !canManage ? 'active' : event.key === 'Home' ? 'active' : event.key === 'End' ? 'inactive' : tab === 'active' ? 'inactive' : 'active'
              document.getElementById(`warehouses-tab-${target}`)?.focus()
            }
          }}>{tab === 'active' ? 'Aktif Depolar' : 'Pasif Depolar'}</button>)}
        </div>
        {canManage && activeTab === 'active' && <button ref={createButtonRef} type="button" className="warehouses-retry" disabled={tabsLocked} aria-expanded={formOpen} aria-controls={formOpen ? 'warehouse-create-form' : undefined} onClick={() => { setSuccessMessage(''); setDeactivationError(''); setFormOpen(true) }}>Yeni Depo Ekle</button>}
      </div>
      {successMessage && <p className="product-create-success" role="status">{successMessage}</p>}
      {deactivationError && <p className="product-form-error" role="alert">{deactivationError}</p>}
      {reactivationError && <p className="product-form-error" role="alert">{reactivationError}</p>}
      {deactivatingId !== null && <p className="warehouse-deactivation-progress" role="status">Depo pasifleştiriliyor...</p>}
      {reactivatingId !== null && <p className="warehouse-deactivation-progress" role="status">Depo aktifleştiriliyor...</p>}
      {formOpen && <WarehouseCreateForm onCancel={() => setFormOpen(false)} onSuccess={() => { setFormOpen(false); setSuccessMessage('Depo başarıyla eklendi.'); retry() }} />}
      {editingWarehouse && <WarehouseEditForm key={editingWarehouse.id} warehouse={editingWarehouse} onCancel={() => setEditingWarehouse(null)} onSuccess={() => { setEditingWarehouse(null); setSuccessMessage('Depo başarıyla güncellendi.'); retry() }} />}
      <div id="warehouses-panel" role="tabpanel" aria-labelledby={`warehouses-tab-${activeTab}`} tabIndex={0}>
      <Card title={activeTab === 'active' ? 'Depo listesi' : 'Pasif depo listesi'} icon={Building2}>
        {state.status === 'loading' && <div className="warehouses-state" role="status">{activeTab === 'active' ? 'Depolar yükleniyor...' : 'Pasif depolar yükleniyor...'}</div>}
        {state.status === 'error' && <div className="warehouses-state"><p role="alert">{state.message}</p><button type="button" className="warehouses-retry" onClick={retry}>Tekrar Dene</button></div>}
        {state.status === 'success' && (state.warehouses.length === 0 ? (
          <EmptyState icon={Building2} title={activeTab === 'active' ? 'Henüz depo eklenmemiş.' : 'Pasifleştirilmiş depo bulunmuyor.'} description={activeTab === 'active' ? 'Depolar eklendiğinde bu alanda listelenecek.' : 'Pasifleştirilen depolar bu alanda listelenecek.'} />
        ) : (
          <div className="warehouses-table-scroll" role="region" aria-label="Depo listesi" tabIndex={0}>
            <table className="warehouses-table">
              <caption>{activeTab === 'active' ? 'Aktif depolar' : 'Pasif depolar'} — {state.warehouses.length} depo</caption>
              <thead><tr>
                <th scope="col">Depo Kodu</th><th scope="col">Depo Adı</th><th scope="col">Konum</th><th scope="col">Durum</th><th scope="col">Oluşturulma Tarihi</th>{canManage && <th scope="col">İşlemler</th>}
              </tr></thead>
              <tbody>{state.warehouses.map((warehouse) => (
                <tr key={warehouse.id}>
                  <td>{warehouse.code}</td>
                  <th scope="row">{warehouse.name}</th>
                  <td className="warehouse-location">{warehouse.location?.trim() || '—'}</td>
                  <td><span className={`warehouse-status ${warehouse.isActive ? 'is-active' : 'is-inactive'}`}>{warehouse.isActive ? 'Aktif' : 'Pasif'}</span></td>
                  <td className="warehouse-date">{formatCreatedAt(warehouse.createdAtUtc)}</td>
                  {canManage && <td><div className="warehouse-row-actions">
                    {activeTab === 'active' ? <>
                      <button type="button" className="warehouse-edit-button" disabled={tabsLocked} aria-label={`${warehouse.code} kodlu ${warehouse.name} deposunu düzenle`} onClick={(event) => { editButtonRef.current = event.currentTarget; setSuccessMessage(''); setDeactivationError(''); setEditingWarehouse(warehouse) }}>Düzenle</button>
                      {warehouse.isActive && <button type="button" className="warehouse-deactivate-button" disabled={tabsLocked} aria-label={`${warehouse.code} kodlu ${warehouse.name} deposunu pasifleştir`} onClick={() => void deactivate(warehouse)}>{deactivatingId === warehouse.id ? 'Pasifleştiriliyor...' : 'Pasifleştir'}</button>}
                    </> : <button type="button" className="warehouse-reactivate-button" disabled={tabsLocked || warehouse.isActive} aria-label={`${warehouse.code} kodlu ${warehouse.name} deposunu aktifleştir`} onClick={() => void reactivate(warehouse)}>{reactivatingId === warehouse.id ? 'Aktifleştiriliyor...' : 'Aktifleştir'}</button>}
                  </div></td>}
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
