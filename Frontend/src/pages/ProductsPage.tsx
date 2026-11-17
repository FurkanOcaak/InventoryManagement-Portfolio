import { roles, useAuth } from '../auth/AuthContext'
import { apiFetch } from '../api/apiFetch'
import { useEffect, useRef, useState } from 'react'
import { Package } from 'lucide-react'
import { Card, EmptyState, PageHeading } from '../components/ui/PageElements'
import { ProductCreateForm } from '../components/products/ProductCreateForm'
import { ProductEditForm } from '../components/products/ProductEditForm'

interface Product {
  id: string
  barcode: string
  barcodeFormat?: string
  name: string
  description: string | null
  unitPrice: number
  minimumStock: number
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string | null
}

type ProductsState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'success'; products: Product[] }

type ProductTab = 'active' | 'inactive'

const priceFormatter = new Intl.NumberFormat('tr-TR', {
  style: 'currency',
  currency: 'TRY',
})

export function ProductsPage() {
  const { user } = useAuth()
  const canManage = user?.roles.includes(roles.admin) ?? false
  const [state, setState] = useState<ProductsState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)
  const [activeTab, setActiveTab] = useState<ProductTab>('active')
  const listRequestRef = useRef<AbortController | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [editingProduct, setEditingProduct] = useState<Product | null>(null)
  const [successMessage, setSuccessMessage] = useState('')
  const [deactivationError, setDeactivationError] = useState('')
  const [deactivatingId, setDeactivatingId] = useState<string | null>(null)
  const deactivationRequestRef = useRef<AbortController | null>(null)
  const [reactivationError, setReactivationError] = useState('')
  const [reactivatingId, setReactivatingId] = useState<string | null>(null)
  const reactivationRequestRef = useRef<AbortController | null>(null)
  const createButtonRef = useRef<HTMLButtonElement>(null)
  const editButtonRef = useRef<HTMLButtonElement | null>(null)

  const actionBusy = deactivatingId !== null || reactivatingId !== null
  const tabsLocked = formOpen || editingProduct !== null || actionBusy

  useEffect(() => () => {
    deactivationRequestRef.current?.abort()
    reactivationRequestRef.current?.abort()
  }, [])

  useEffect(() => {
    if (!editingProduct && editButtonRef.current) {
      const target = editButtonRef.current.isConnected ? editButtonRef.current : createButtonRef.current
      target?.focus()
      editButtonRef.current = null
    }
  }, [editingProduct])

  useEffect(() => {
    const controller = new AbortController()
    listRequestRef.current = controller

    async function loadProducts() {
      try {
        const response = await apiFetch(activeTab === 'active' ? '/api/products' : '/api/products/inactive', { signal: controller.signal })
        if (!response.ok) {
          throw new Error(`Ürünler yüklenemedi (HTTP ${response.status}). Lütfen tekrar deneyin.`)
        }

        const products: Product[] = await response.json()
        if (!Array.isArray(products)) {
          throw new Error('Sunucudan beklenen ürün listesi alınamadı. Lütfen tekrar deneyin.')
        }

        if (!controller.signal.aborted) setState({ status: 'success', products })
      } catch (error: unknown) {
        if (controller.signal.aborted) return
        const message = error instanceof TypeError
          ? 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.'
          : error instanceof SyntaxError
            ? 'Sunucudan geçerli bir ürün yanıtı alınamadı. Lütfen tekrar deneyin.'
            : error instanceof Error
              ? error.message
              : 'Ürünler yüklenirken beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.'
        setState({ status: 'error', message })
      }
    }

    void loadProducts()
    return () => controller.abort()
  }, [attempt, activeTab])

  function retry() {
    listRequestRef.current?.abort()
    setState({ status: 'loading' })
    setAttempt((previous) => previous + 1)
  }

  function selectTab(tab: ProductTab) {
    if (tab === 'inactive' && !canManage) return
    if (tab === activeTab || tabsLocked || deactivationRequestRef.current || reactivationRequestRef.current) return
    setSuccessMessage('')
    setDeactivationError('')
    setReactivationError('')
    setActiveTab(tab)
    retry()
  }

  async function reactivate(product: Product) {
    if (!canManage) return
    if (reactivationRequestRef.current || deactivationRequestRef.current || formOpen || editingProduct || activeTab !== 'inactive' || product.isActive) return
    if (!window.confirm(`${product.barcode} kodlu "${product.name}" ürününü yeniden aktifleştirmek istediğinize emin misiniz? Ürün aktif listede yeniden görünecek.`)) return

    const controller = new AbortController()
    reactivationRequestRef.current = controller
    setReactivatingId(product.id)
    setSuccessMessage('')
    setReactivationError('')
    try {
      const response = await apiFetch(`/api/products/${encodeURIComponent(product.id)}/reactivate`, {
        method: 'PATCH',
        signal: controller.signal,
      })
      if (controller.signal.aborted) return
      if (response.status === 200) {
        setSuccessMessage('Ürün başarıyla aktifleştirildi.')
        retry()
      } else if (response.status === 404) {
        setReactivationError('Ürün artık bulunamıyor. Pasif ürün listesi yeniden yükleniyor.')
        retry()
      } else if (response.status === 409) {
        setReactivationError('Ürün zaten aktif. Pasif ürün listesi yeniden yükleniyor.')
        retry()
      } else {
        setReactivationError(`Ürün aktifleştirilemedi (HTTP ${response.status}). Ürün listelerini kontrol ettikten sonra tekrar deneyin.`)
      }
    } catch {
      if (!controller.signal.aborted) {
        setReactivationError('Sunucuya ulaşılamadı veya yanıt alınamadı. Bağlantınızı ve ürün listelerini kontrol ettikten sonra tekrar deneyin.')
      }
    } finally {
      if (!controller.signal.aborted) {
        reactivationRequestRef.current = null
        setReactivatingId(null)
      }
    }
  }

  async function deactivate(product: Product) {
    if (!canManage) return
    if (deactivationRequestRef.current || reactivationRequestRef.current || formOpen || editingProduct || activeTab !== 'active' || !product.isActive) return
    if (!window.confirm(`${product.barcode} kodlu "${product.name}" ürününü pasifleştirmek istediğinize emin misiniz? Ürün aktif listeden kaldırılacak.`)) return

    const controller = new AbortController()
    deactivationRequestRef.current = controller
    setDeactivatingId(product.id)
    setSuccessMessage('')
    setDeactivationError('')
    try {
      const response = await apiFetch(`/api/products/${encodeURIComponent(product.id)}`, {
        method: 'DELETE',
        signal: controller.signal,
      })
      if (controller.signal.aborted) return
      if (response.status === 204) {
        setSuccessMessage('Ürün başarıyla pasifleştirildi.')
        retry()
      } else if (response.status === 404) {
        setDeactivationError('Ürün artık bulunamıyor. Ürün listesi yeniden yükleniyor.')
        retry()
      } else {
        setDeactivationError(`Ürün pasifleştirilemedi (HTTP ${response.status}). Ürün listesini kontrol ettikten sonra tekrar deneyin.`)
      }
    } catch {
      if (!controller.signal.aborted) {
        setDeactivationError('Sunucuya ulaşılamadı veya yanıt alınamadı. Bağlantınızı ve ürün listesini kontrol ettikten sonra tekrar deneyin.')
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
      <PageHeading eyebrow="ENVANTER YÖNETİMİ" title="Ürünler" description="Ürün kataloğunuzu tek bir yerden takip edin." />
      <div className="products-toolbar">
        <div className="product-tabs" role="tablist" aria-label="Ürün durumu">
          {(canManage ? ['active', 'inactive'] as const : ['active'] as const).map((tab) => <button key={tab} id={`products-tab-${tab}`} type="button" role="tab" aria-selected={activeTab === tab} aria-controls="products-panel" tabIndex={activeTab === tab ? 0 : -1} disabled={tabsLocked} onClick={() => selectTab(tab)} onKeyDown={(event) => {
            if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
              event.preventDefault()
              const target = !canManage ? 'active' : event.key === 'Home' ? 'active' : event.key === 'End' ? 'inactive' : tab === 'active' ? 'inactive' : 'active'
              document.getElementById(`products-tab-${target}`)?.focus()
            }
          }}>{tab === 'active' ? 'Aktif Ürünler' : 'Pasif Ürünler'}</button>)}
        </div>
        {canManage && activeTab === 'active' && <button ref={createButtonRef} type="button" className="products-retry" disabled={tabsLocked} aria-expanded={formOpen} aria-controls={formOpen ? 'product-create-form' : undefined} onClick={() => { setSuccessMessage(''); setDeactivationError(''); setFormOpen(true) }}>Yeni Ürün Ekle</button>}
      </div>
      {successMessage && <p className="product-create-success" role="status">{successMessage}</p>}
      {deactivationError && <p className="product-form-error" role="alert">{deactivationError}</p>}
      {reactivationError && <p className="product-form-error" role="alert">{reactivationError}</p>}
      {deactivatingId !== null && <p className="product-deactivation-progress" role="status">Ürün pasifleştiriliyor...</p>}
      {reactivatingId !== null && <p className="product-deactivation-progress" role="status">Ürün aktifleştiriliyor...</p>}
      {formOpen && <ProductCreateForm onCancel={() => { setFormOpen(false); createButtonRef.current?.focus() }} onSuccess={() => { setFormOpen(false); setSuccessMessage('Ürün başarıyla eklendi.'); retry(); createButtonRef.current?.focus() }} />}
      {editingProduct && <ProductEditForm key={editingProduct.id} product={editingProduct} onCancel={() => setEditingProduct(null)} onSuccess={() => { setEditingProduct(null); setSuccessMessage('Ürün başarıyla güncellendi.'); retry() }} />}
      <div id="products-panel" role="tabpanel" aria-labelledby={`products-tab-${activeTab}`} tabIndex={0}>
      <Card title={activeTab === 'active' ? 'Ürün kataloğu' : 'Pasif ürün kataloğu'} icon={Package}>
        {state.status === 'loading' && (
          <div className="products-state" role="status">{activeTab === 'active' ? 'Ürünler yükleniyor...' : 'Pasif ürünler yükleniyor...'}</div>
        )}
        {state.status === 'error' && (
          <div className="products-state">
            <p role="alert">{state.message}</p>
            <button type="button" className="products-retry" onClick={retry}>Tekrar Dene</button>
          </div>
        )}
        {state.status === 'success' && (state.products.length === 0 ? (
          <EmptyState icon={Package} title={activeTab === 'active' ? 'Henüz ürün eklenmemiş.' : 'Pasifleştirilmiş ürün bulunmuyor.'} description={activeTab === 'active' ? 'Ürünler eklendiğinde bu alanda listelenecek.' : 'Pasifleştirilen ürünler bu alanda listelenecek.'} />
        ) : (
          <div className="products-table-scroll" role="region" aria-label="Ürün listesi" tabIndex={0}>
            <table className="products-table">
              <caption>{activeTab === 'active' ? 'Aktif ürünler' : 'Pasif ürünler'} — {state.products.length} ürün</caption>
              <thead><tr>
                <th scope="col">Ürün Barkodu</th>
                <th scope="col">Ürün Adı</th>
                <th scope="col">Açıklama</th>
                <th scope="col" className="products-number">Birim Fiyat</th>
                <th scope="col" className="products-number">Minimum Stok</th>
                <th scope="col">Durum</th>
                {canManage && <th scope="col">İşlemler</th>}
              </tr></thead>
              <tbody>{state.products.map((product) => (
                <tr key={product.id}>
                  <td>{product.barcode}<p className="product-form-hint">{product.barcodeFormat === 'GtinChecksumValid' ? 'GTIN biçimi ve kontrol basamağı uygun; GS1 tahsisi doğrulanmadı.' : product.barcodeFormat === 'GtinChecksumInvalid' ? 'GTIN kontrol basamağı uygun değil; mevcut kimlik korundu.' : 'Dahili / eski ürün kimliği; GS1 doğrulaması yok.'}</p></td>
                  <th scope="row">{product.name}</th>
                  <td className="products-description">{product.description ?? '—'}</td>
                  <td className="products-number">{priceFormatter.format(product.unitPrice)}</td>
                  <td className="products-number">{product.minimumStock}</td>
                  <td><span className={`products-status ${product.isActive ? 'is-active' : 'is-inactive'}`}>{product.isActive ? 'Aktif' : 'Pasif'}</span></td>
                  {canManage && <td><div className="product-row-actions">
                    {activeTab === 'active' ? <>
                      <button type="button" className="product-edit-button" disabled={tabsLocked} aria-label={`${product.name} ürününü düzenle`} onClick={(event) => { editButtonRef.current = event.currentTarget; setSuccessMessage(''); setDeactivationError(''); setEditingProduct(product) }}>Düzenle</button>
                      <button type="button" className="product-deactivate-button" disabled={tabsLocked || !product.isActive} aria-label={`${product.barcode} kodlu ${product.name} ürününü pasifleştir`} onClick={() => void deactivate(product)}>{deactivatingId === product.id ? 'Pasifleştiriliyor...' : 'Pasifleştir'}</button>
                    </> : <button type="button" className="product-reactivate-button" disabled={actionBusy || product.isActive} aria-label={`${product.barcode} kodlu ${product.name} ürününü aktifleştir`} onClick={() => void reactivate(product)}>{reactivatingId === product.id ? 'Aktifleştiriliyor...' : 'Aktifleştir'}</button>}
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
