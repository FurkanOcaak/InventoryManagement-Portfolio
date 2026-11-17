import { roles, useAuth } from '../auth/AuthContext'
import { apiFetch } from '../api/apiFetch'
import { useEffect, useRef, useState } from 'react'
import { Boxes } from 'lucide-react'
import { Card, EmptyState, PageHeading } from '../components/ui/PageElements'
import { StockInForm } from '../components/stock/StockInForm'
import { StockOutForm } from '../components/stock/StockOutForm'

interface InventoryStockItemResponse {
  inventoryId: string
  productId: string
  productBarcode: string
  productName: string
  warehouseId: string
  warehouseCode: string
  warehouseName: string
  quantity: number
  minimumStock: number
}

type StockState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'success'; items: InventoryStockItemResponse[] }

const quantityFormatter = new Intl.NumberFormat('tr-TR')

export function StockPage() {
  const { user } = useAuth()
  const canWrite = user?.roles.some((role) => role === roles.admin || role === roles.staff) ?? false
  const [state, setState] = useState<StockState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)
  const [formOpen, setFormOpen] = useState(false)
  const [outFormOpen, setOutFormOpen] = useState(false)
  const [successMessage, setSuccessMessage] = useState('')
  const openButtonRef = useRef<HTMLButtonElement>(null)
  const wasFormOpenRef = useRef(false)
  const outButtonRef = useRef<HTMLButtonElement>(null)
  const wasOutFormOpenRef = useRef(false)

  useEffect(() => {
    if (!outFormOpen && wasOutFormOpenRef.current) outButtonRef.current?.focus()
    wasOutFormOpenRef.current = outFormOpen
  }, [outFormOpen])

  useEffect(() => {
    if (!formOpen && wasFormOpenRef.current) openButtonRef.current?.focus()
    wasFormOpenRef.current = formOpen
  }, [formOpen])

  useEffect(() => {
    const controller = new AbortController()

    async function loadStock() {
      try {
        const response = await apiFetch('/api/stock', { signal: controller.signal })
        if (!response.ok) {
          throw new Error(`Stok kayıtları yüklenemedi (HTTP ${response.status}). Lütfen tekrar deneyin.`)
        }
        const items: InventoryStockItemResponse[] = await response.json()
        if (!Array.isArray(items)) {
          throw new Error('Sunucudan beklenen stok listesi alınamadı. Lütfen tekrar deneyin.')
        }
        if (!controller.signal.aborted) setState({ status: 'success', items })
      } catch (error: unknown) {
        if (controller.signal.aborted) return
        const message = error instanceof TypeError
          ? 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.'
          : error instanceof SyntaxError
            ? 'Sunucudan geçerli bir stok yanıtı alınamadı. Lütfen tekrar deneyin.'
            : error instanceof Error
              ? error.message
              : 'Stok kayıtları yüklenirken beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.'
        setState({ status: 'error', message })
      }
    }

    void loadStock()
    return () => controller.abort()
  }, [attempt])

  function retry() {
    setState({ status: 'loading' })
    setAttempt((previous) => previous + 1)
  }

  return (
    <>
      <PageHeading eyebrow="STOK YÖNETİMİ" title="Stok İşlemleri" description="Ürün ve depo bazındaki güncel stok miktarlarını takip edin." />
      {canWrite && <div className="stock-toolbar">
        <button ref={openButtonRef} type="button" className="stock-retry" disabled={formOpen || outFormOpen} aria-expanded={formOpen} aria-controls={formOpen ? 'stock-in-form' : undefined} onClick={() => { if (!formOpen && !outFormOpen) { setSuccessMessage(''); setFormOpen(true) } }}>Stok Girişi</button>
        <button ref={outButtonRef} type="button" className="stock-out-button" disabled={formOpen || outFormOpen} aria-expanded={outFormOpen} aria-controls={outFormOpen ? 'stock-out-form' : undefined} onClick={() => { if (!formOpen && !outFormOpen) { setSuccessMessage(''); setOutFormOpen(true) } }}>Stok Çıkışı</button>
      </div>}
      {successMessage && <p className="product-create-success" role="status">{successMessage}</p>}
      {canWrite && formOpen && <StockInForm onCancel={() => setFormOpen(false)} onSuccess={() => { setFormOpen(false); setSuccessMessage('Stok girişi başarıyla kaydedildi.'); retry() }} />}
      {canWrite && outFormOpen && <StockOutForm onCancel={() => setOutFormOpen(false)} onSuccess={() => { setOutFormOpen(false); setSuccessMessage('Stok çıkışı başarıyla kaydedildi.'); retry() }} />}
      <Card title="Güncel stok listesi" icon={Boxes}>
        {state.status === 'loading' && <div className="stock-state" role="status">Stok kayıtları yükleniyor...</div>}
        {state.status === 'error' && <div className="stock-state"><p role="alert">{state.message}</p><button type="button" className="stock-retry" onClick={retry}>Tekrar Dene</button></div>}
        {state.status === 'success' && (state.items.length === 0 ? (
          <EmptyState icon={Boxes} title="Henüz stok kaydı bulunmuyor." description="İlk stok girişinizi yaptığınızda ürünler burada görüntülenecek." />
        ) : (
          <div className="stock-table-scroll" role="region" aria-label="Güncel stok listesi" tabIndex={0}>
            <table className="stock-table">
              <caption>Güncel stok — {state.items.length} kayıt</caption>
              <thead><tr>
                <th scope="col">Ürün Barkodu</th>
                <th scope="col">Ürün Adı</th>
                <th scope="col">Depo Kodu</th>
                <th scope="col">Depo Adı</th>
                <th scope="col" className="stock-number">Mevcut Miktar</th>
                <th scope="col" className="stock-number">Minimum Stok</th>
                <th scope="col">Durum</th>
              </tr></thead>
              <tbody>{state.items.map((item) => {
                const lowStock = item.quantity <= item.minimumStock
                return (
                  <tr key={item.inventoryId}>
                    <td>{item.productBarcode}</td>
                    <th scope="row">{item.productName}</th>
                    <td>{item.warehouseCode}</td>
                    <td>{item.warehouseName}</td>
                    <td className="stock-number">{quantityFormatter.format(item.quantity)}</td>
                    <td className="stock-number">{quantityFormatter.format(item.minimumStock)}</td>
                    <td><span className={`stock-status ${lowStock ? 'is-low' : 'is-normal'}`}>{lowStock ? 'Düşük Stok' : 'Normal'}</span></td>
                  </tr>
                )
              })}</tbody>
            </table>
          </div>
        ))}
      </Card>
    </>
  )
}
