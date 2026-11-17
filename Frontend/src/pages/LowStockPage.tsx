import { apiFetch } from '../api/apiFetch'
import { useEffect, useState } from 'react'
import { TriangleAlert } from 'lucide-react'
import { Card, EmptyState, PageHeading } from '../components/ui/PageElements'

interface LowStockItemResponse {
  productId: string
  productBarcode: string
  productName: string
  warehouseId: string
  warehouseName: string
  quantity: number
  minimumStock: number
  shortage: number
}

type LowStockState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'success'; items: LowStockItemResponse[] }

const quantityFormatter = new Intl.NumberFormat('tr-TR')

function formatQuantity(value: number) {
  return typeof value === 'number' && Number.isFinite(value) ? quantityFormatter.format(value) : '—'
}

function stockPresentation(quantity: number, minimumStock: number) {
  if (Number.isFinite(quantity) && Number.isFinite(minimumStock) && quantity >= 0 && minimumStock >= 0) {
    if (quantity < minimumStock) return { label: 'Düşük Stok', className: 'is-low' }
    if (quantity === minimumStock) return { label: 'Eşik Seviyesinde', className: 'is-threshold' }
  }
  return { label: 'Kontrol Edilmeli', className: 'is-unknown' }
}

export function LowStockPage() {
  const [state, setState] = useState<LowStockState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    async function loadLowStock() {
      try {
        const response = await apiFetch('/api/stock/low-stock', { signal: controller.signal })
        if (!response.ok) throw new Error(`Düşük stok kayıtları yüklenemedi (HTTP ${response.status}). Lütfen tekrar deneyin.`)
        const items: LowStockItemResponse[] = await response.json()
        if (!Array.isArray(items)) throw new Error('Sunucudan beklenen düşük stok listesi alınamadı. Lütfen tekrar deneyin.')
        if (!controller.signal.aborted) setState({ status: 'success', items })
      } catch (error: unknown) {
        if (controller.signal.aborted) return
        const message = error instanceof TypeError
          ? 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.'
          : error instanceof SyntaxError
            ? 'Sunucudan geçerli bir düşük stok yanıtı alınamadı. Lütfen tekrar deneyin.'
            : error instanceof Error
              ? error.message
              : 'Düşük stok kayıtları yüklenirken beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.'
        setState({ status: 'error', message })
      }
    }
    void loadLowStock()
    return () => controller.abort()
  }, [attempt])

  function retry() {
    setState({ status: 'loading' })
    setAttempt((previous) => previous + 1)
  }

  return (
    <>
      <PageHeading eyebrow="STOK YÖNETİMİ" title="Düşük Stok" description="Minimum stok seviyesindeki ve altındaki kayıtları takip edin." />
      <div className="low-stock-summary" aria-label="API yanıtındaki uyarı sayısı"><TriangleAlert size={24} aria-hidden="true" /><div><span>Uyarı Sayısı</span><strong>{state.status === 'success' ? quantityFormatter.format(state.items.length) : '—'}</strong></div></div>
      <Card title="Düşük stok uyarıları" icon={TriangleAlert}>
        <p className="low-stock-scope">Yalnızca mevcut stok kayıtları değerlendirilir. Stok kaydı olmayan ürün-depo çiftleri bu listenin kapsamı dışındadır.</p>
        {state.status === 'loading' && <div className="low-stock-state" role="status">Düşük stok kayıtları yükleniyor...</div>}
        {state.status === 'error' && <div className="low-stock-state"><p role="alert">{state.message}</p><button type="button" className="low-stock-retry" onClick={retry}>Tekrar Dene</button></div>}
        {state.status === 'success' && (state.items.length === 0 ? (
          <EmptyState icon={TriangleAlert} title="Kayıtlı stoklarda düşük stok uyarısı bulunmuyor." description="Stok kaydı hiç olmayan ürün-depo çiftleri bu sonuçlara dahil değildir." />
        ) : (
          <div className="low-stock-table-scroll" role="region" aria-label="Düşük stok listesi" tabIndex={0}>
            <table className="low-stock-table">
              <caption>API yanıtında {quantityFormatter.format(state.items.length)} stok uyarısı bulunuyor.</caption>
              <thead><tr>
                <th scope="col">Ürün Barkodu</th><th scope="col">Ürün Adı</th><th scope="col">Depo</th><th scope="col" className="low-stock-number">Mevcut Stok</th><th scope="col" className="low-stock-number">Minimum Stok</th><th scope="col" className="low-stock-number">Eksik Miktar</th><th scope="col">Durum</th>
              </tr></thead>
              <tbody>{state.items.map((item) => {
                const presentation = stockPresentation(item.quantity, item.minimumStock)
                return <tr key={`${item.productId}:${item.warehouseId}`}>
                  <td>{item.productBarcode}</td>
                  <th scope="row">{item.productName}</th>
                  <td>{item.warehouseName}</td>
                  <td className="low-stock-number">{formatQuantity(item.quantity)}</td>
                  <td className="low-stock-number">{formatQuantity(item.minimumStock)}</td>
                  <td className="low-stock-number">{formatQuantity(item.shortage)}</td>
                  <td><span className={`low-stock-status ${presentation.className}`}>{presentation.label}</span></td>
                </tr>
              })}</tbody>
            </table>
          </div>
        ))}
      </Card>
    </>
  )
}
