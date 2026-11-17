import { apiFetch } from '../api/apiFetch'
import { useEffect, useState } from 'react'
import { ArrowLeftRight } from 'lucide-react'
import { Card, EmptyState, PageHeading } from '../components/ui/PageElements'

interface StockMovementHistoryResponse {
  stockMovementId: string
  productId: string
  productBarcode: string
  warehouseId: string
  warehouseCode: string
  movementType: number
  quantity: number
  quantityBefore: number
  quantityAfter: number
  note: string | null
  createdAtUtc: string
}

type MovementsState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'success'; items: StockMovementHistoryResponse[] }

const quantityFormatter = new Intl.NumberFormat('tr-TR')
const dateFormatter = new Intl.DateTimeFormat('tr-TR', {
  dateStyle: 'medium',
  timeStyle: 'medium',
  timeZone: 'Europe/Istanbul',
})

function formatCreatedAt(value: string) {
  if (typeof value !== 'string' || !value.trim()) return '—'
  const text = value.trim()
  // UTC timestamps from SQL can lack the explicit timezone suffix.
  const date = new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(text) ? text : `${text}Z`)
  return Number.isNaN(date.getTime()) ? '—' : dateFormatter.format(date)
}

function movementPresentation(type: number) {
  switch (type) {
    case 1: return { label: 'Stok Girişi', sign: '+', className: 'is-in' }
    case 2: return { label: 'Stok Çıkışı', sign: '−', className: 'is-out' }
    default: return { label: 'Bilinmeyen İşlem', sign: '', className: 'is-unknown' }
  }
}

export function MovementsPage() {
  const [state, setState] = useState<MovementsState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    async function loadMovements() {
      try {
        const response = await apiFetch('/api/stock/movements?limit=50', { signal: controller.signal })
        if (!response.ok) throw new Error(`Stok hareketleri yüklenemedi (HTTP ${response.status}). Lütfen tekrar deneyin.`)
        const items: StockMovementHistoryResponse[] = await response.json()
        if (!Array.isArray(items)) throw new Error('Sunucudan beklenen stok hareketleri listesi alınamadı. Lütfen tekrar deneyin.')
        if (!controller.signal.aborted) setState({ status: 'success', items })
      } catch (error: unknown) {
        if (controller.signal.aborted) return
        const message = error instanceof TypeError
          ? 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.'
          : error instanceof SyntaxError
            ? 'Sunucudan geçerli bir stok hareketi yanıtı alınamadı. Lütfen tekrar deneyin.'
            : error instanceof Error
              ? error.message
              : 'Stok hareketleri yüklenirken beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.'
        setState({ status: 'error', message })
      }
    }
    void loadMovements()
    return () => controller.abort()
  }, [attempt])

  function retry() {
    setState({ status: 'loading' })
    setAttempt((previous) => previous + 1)
  }

  return (
    <>
      <PageHeading eyebrow="STOK YÖNETİMİ" title="Stok Hareketleri" description="Envanterinizdeki giriş ve çıkışların geçmişini takip edin." />
      <Card title="Hareket geçmişi" icon={ArrowLeftRight}>
        <p className="movements-limit">En yeni kayıtlar önce gösterilir. Bu görünüm son 50 stok hareketiyle sınırlıdır.</p>
        {state.status === 'loading' && <div className="movements-state" role="status">Stok hareketleri yükleniyor...</div>}
        {state.status === 'error' && <div className="movements-state"><p role="alert">{state.message}</p><button type="button" className="movements-retry" onClick={retry}>Tekrar Dene</button></div>}
        {state.status === 'success' && (state.items.length === 0 ? (
          <EmptyState icon={ArrowLeftRight} title="Henüz stok hareketi bulunmuyor." description="Stok giriş ve çıkışları yapıldığında hareketler burada görüntülenecek." />
        ) : (
          <div className="movements-table-scroll" role="region" aria-label="Stok hareketleri geçmişi" tabIndex={0}>
            <table className="movements-table">
              <caption>Son 50 hareket içinden {state.items.length} kayıt gösteriliyor.</caption>
              <thead><tr>
                <th scope="col">Tarih / Saat</th><th scope="col">Ürün Barkodu</th><th scope="col">Depo Kodu</th><th scope="col">İşlem Türü</th><th scope="col" className="movements-number">İşlem Miktarı</th><th scope="col" className="movements-number">Önceki Stok</th><th scope="col" className="movements-number">Sonraki Stok</th><th scope="col">Açıklama</th>
              </tr></thead>
              <tbody>{state.items.map((item) => {
                const presentation = movementPresentation(item.movementType)
                return <tr key={item.stockMovementId}>
                  <td className="movements-date">{formatCreatedAt(item.createdAtUtc)}</td>
                  <th scope="row">{item.productBarcode}</th>
                  <td>{item.warehouseCode}</td>
                  <td><span className={`movement-type ${presentation.className}`}>{presentation.label}</span></td>
                  <td className={`movements-number movement-quantity ${presentation.className}`}>{presentation.sign}{quantityFormatter.format(item.quantity)}</td>
                  <td className="movements-number">{quantityFormatter.format(item.quantityBefore)}</td>
                  <td className="movements-number">{quantityFormatter.format(item.quantityAfter)}</td>
                  <td className="movements-note">{item.note?.trim() || '—'}</td>
                </tr>
              })}</tbody>
            </table>
          </div>
        ))}
      </Card>
    </>
  )
}
