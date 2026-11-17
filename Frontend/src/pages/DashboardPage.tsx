import { apiFetch } from '../api/apiFetch'
import { useEffect, useRef, useState } from 'react'
import { ArrowDownLeft, ArrowUpRight, ArrowLeftRight, Building2, ChartNoAxesCombined, Layers3, Package, RefreshCw, ShieldCheck, TriangleAlert } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Card, EmptyState, PageHeading } from '../components/ui/PageElements'

type MetricKey = 'products' | 'warehouses' | 'stock' | 'lowStock'
type MetricState = { status: 'loading' } | { status: 'error'; message: string } | { status: 'success'; value: number }

interface RecentMovement {
  stockMovementId: string
  productBarcode: string
  warehouseCode: string
  movementType: unknown
  quantity: number
  quantityBefore: number
  quantityAfter: number
  createdAtUtc: unknown
}

interface CriticalStock {
  productId: string
  productBarcode: string
  productName: string
  warehouseId: string
  warehouseName: string
  quantity: number
  minimumStock: number
  shortage: number
}

type ListState<T> = { status: 'loading' } | { status: 'error'; message: string } | { status: 'success'; items: T[] }

const numberFormatter = new Intl.NumberFormat('tr-TR')
const dateFormatter = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'medium', timeZone: 'Europe/Istanbul' })

function formatMovementDate(value: unknown) {
  if (typeof value !== 'string' || !value.trim()) return '—'
  const text = value.trim()
  const date = new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(text) ? text : `${text}Z`)
  return Number.isNaN(date.getTime()) ? '—' : dateFormatter.format(date)
}

function movementPresentation(type: unknown) {
  if (type === 1) return { label: 'Stok Girişi', sign: '+', className: 'is-in' }
  if (type === 2) return { label: 'Stok Çıkışı', sign: '−', className: 'is-out' }
  return { label: 'Bilinmeyen İşlem', sign: '', className: 'is-unknown' }
}

function criticalPresentation(quantity: number, minimumStock: number) {
  if (quantity < minimumStock) return { label: 'Düşük Stok', className: 'is-low' }
  if (quantity === minimumStock) return { label: 'Eşik Seviyesinde', className: 'is-threshold' }
  return { label: 'Kontrol Edilmeli', className: 'is-unknown' }
}

function parseRecentMovements(data: unknown): RecentMovement[] {
  if (!Array.isArray(data) || !data.every((item) => item && typeof item === 'object'
    && ['stockMovementId', 'productBarcode', 'warehouseCode'].every((field) => typeof item[field] === 'string' && item[field].trim())
    && ['quantity', 'quantityBefore', 'quantityAfter'].every((field) => Number.isSafeInteger(item[field]) && item[field] >= 0))) {
    throw new Error('Sunucudan geçerli stok hareketleri alınamadı.')
  }
  return data as RecentMovement[]
}

function parseCriticalStock(data: unknown): CriticalStock[] {
  if (!Array.isArray(data) || !data.every((item) => item && typeof item === 'object'
    && ['productId', 'productBarcode', 'productName', 'warehouseId', 'warehouseName'].every((field) => typeof item[field] === 'string' && item[field].trim())
    && ['quantity', 'minimumStock', 'shortage'].every((field) => Number.isSafeInteger(item[field]) && item[field] >= 0))) {
    throw new Error('Sunucudan geçerli kritik stok kayıtları alınamadı.')
  }
  return data as CriticalStock[]
}

function loadingMetrics(): Record<MetricKey, MetricState> {
  return { products: { status: 'loading' }, warehouses: { status: 'loading' }, stock: { status: 'loading' }, lowStock: { status: 'loading' } }
}

function metricValue(key: MetricKey, data: unknown): number {
  if (!Array.isArray(data)) throw new Error('Sunucudan geçerli bir liste alınamadı.')
  if (!data.every((item) => item !== null && typeof item === 'object' && !Array.isArray(item))) {
    throw new Error('API yanıtındaki kayıtlar geçersiz.')
  }
  if (key === 'stock') {
    const total = data.reduce((sum: number, item: Record<string, unknown>) => {
      if (typeof item.quantity !== 'number' || !Number.isSafeInteger(item.quantity) || item.quantity < 0) {
        throw new Error('API yanıtındaki stok miktarı geçersiz.')
      }
      const next = sum + item.quantity
      if (!Number.isSafeInteger(next)) throw new Error('Toplam stok miktarı güvenli sayı sınırını aşıyor.')
      return next
    }, 0)
    return total
  }
  const idFields = key === 'lowStock' ? ['productId', 'warehouseId'] : ['id']
  if (!data.every((item) => idFields.every((field) => typeof item[field] === 'string' && item[field].trim()))) {
    throw new Error('API yanıtındaki kayıt kimlikleri geçersiz.')
  }
  return data.length
}

const metrics = [
  { key: 'products', endpoint: '/api/products', label: 'Toplam Ürün', icon: Package, detail: 'Aktif ürünler', tone: 'teal' },
  { key: 'warehouses', endpoint: '/api/warehouses', label: 'Toplam Depo', icon: Building2, detail: 'Aktif depolar', tone: 'blue' },
  { key: 'stock', endpoint: '/api/stock', label: 'Toplam Stok', icon: Layers3, detail: 'Depolardaki toplam miktar', tone: 'violet' },
  { key: 'lowStock', endpoint: '/api/stock/low-stock', label: 'Düşük Stok Uyarıları', icon: TriangleAlert, detail: 'Kayıtlı stoklardaki uyarılar', tone: 'amber' },
] as const

export function DashboardPage() {
  const [values, setValues] = useState(loadingMetrics)
  const [attempt, setAttempt] = useState(0)
  const [movements, setMovements] = useState<ListState<RecentMovement>>({ status: 'loading' })
  const [criticalStock, setCriticalStock] = useState<ListState<CriticalStock>>({ status: 'loading' })
  const requestRef = useRef<AbortController | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    requestRef.current = controller
    async function loadMetric(key: MetricKey, endpoint: string) {
      try {
        const response = await apiFetch(endpoint, { signal: controller.signal })
        if (!response.ok) throw new Error(`Veri alınamadı (HTTP ${response.status}).`)
        const data: unknown = await response.json()
        const value = metricValue(key, data)
        if (controller.signal.aborted) return
        setValues((previous) => ({ ...previous, [key]: { status: 'success', value } }))
        if (key === 'lowStock') {
          try {
            setCriticalStock({ status: 'success', items: parseCriticalStock(data) })
          } catch (error: unknown) {
            setCriticalStock({ status: 'error', message: error instanceof Error ? error.message : 'Kritik stok kayıtları alınamadı.' })
          }
        }
      } catch (error: unknown) {
        if (controller.signal.aborted) return
        const message = error instanceof TypeError ? 'Sunucuya ulaşılamadı.'
          : error instanceof SyntaxError ? 'Sunucudan geçerli JSON alınamadı.'
            : error instanceof Error ? error.message : 'Veri yüklenirken bir hata oluştu.'
        setValues((previous) => ({ ...previous, [key]: { status: 'error', message } }))
        if (key === 'lowStock') setCriticalStock({ status: 'error', message })
      }
    }
    async function loadMovements() {
      try {
        const response = await apiFetch('/api/stock/movements?limit=5', { signal: controller.signal })
        if (!response.ok) throw new Error(`Hareketler alınamadı (HTTP ${response.status}).`)
        const data: unknown = await response.json()
        const items = parseRecentMovements(data)
        if (!controller.signal.aborted) setMovements({ status: 'success', items: items.slice(0, 5) })
      } catch (error: unknown) {
        if (controller.signal.aborted) return
        const message = error instanceof TypeError ? 'Sunucuya ulaşılamadı.'
          : error instanceof SyntaxError ? 'Sunucudan geçerli JSON alınamadı.'
            : error instanceof Error ? error.message : 'Stok hareketleri yüklenemedi.'
        setMovements({ status: 'error', message })
      }
    }
    for (const metric of metrics) void loadMetric(metric.key, metric.endpoint)
    void loadMovements()
    return () => controller.abort()
  }, [attempt])

  function refresh() {
    requestRef.current?.abort()
    setValues(loadingMetrics())
    setMovements({ status: 'loading' })
    setCriticalStock({ status: 'loading' })
    setAttempt((previous) => previous + 1)
  }

  return <>
    <div className="dashboard-heading"><PageHeading eyebrow="ÇALIŞMA ALANINIZA HOŞ GELDİNİZ" title="Genel Bakış" description="Envanterinizin genel durumunu tek bir yerden takip edin." /><button type="button" className="dashboard-refresh" onClick={refresh}><RefreshCw size={16} aria-hidden="true" />Yenile</button></div>
    <section className="metrics-grid" aria-label="Envanter özeti">{metrics.map(({ key, label, icon: Icon, detail, tone }) => {
      const metric = values[key]
      return <article className="metric-card" key={key} aria-label={label} aria-busy={metric.status === 'loading'}>
        <div className="metric-top"><span>{label}</span><span className={`metric-icon ${tone}`}><Icon size={20} strokeWidth={1.8} aria-hidden="true" /></span></div>
        <div className="metric-value">{metric.status === 'success' ? numberFormatter.format(metric.value) : '—'}</div>
        {metric.status === 'loading' && <p className="metric-feedback" role="status">{label}: Yükleniyor...</p>}
        {metric.status === 'error' && <p className="metric-feedback is-error" role="alert">{label}: {metric.message} Yenile ile tekrar deneyin.</p>}
        <p><span className={`metric-dot ${tone}`} />{detail}</p>
      </article>
    })}</section>
    <div className="section-intro"><div><span className="eyebrow">OPERASYON TAKİBİ</span><h2>Envanterinize yakından bakın</h2></div><span className="subtle-label">Son hareketler ve kayıtlı stok uyarıları</span></div>
    <div className="dashboard-grid">
      <Card title="Son Stok Hareketleri" icon={ArrowLeftRight} link={{ to: '/movements', label: 'Tüm hareketler' }}>
        <div className="movement-legend"><span><ArrowDownLeft size={14} aria-hidden="true" />Stok girişi</span><span><ArrowUpRight size={14} aria-hidden="true" />Stok çıkışı</span></div>
        {movements.status === 'loading' && <p className="dashboard-section-state" role="status">Son stok hareketleri yükleniyor...</p>}
        {movements.status === 'error' && <p className="dashboard-section-state is-error" role="alert">{movements.message} Yenile ile tekrar deneyin.</p>}
        {movements.status === 'success' && (movements.items.length === 0 ? <EmptyState icon={ArrowLeftRight} title="Henüz stok hareketi bulunmuyor." description="Stok giriş ve çıkışları yapıldığında burada görüntülenecek." compact /> : (
          <ul className="dashboard-movements">{movements.items.map((item) => {
            const presentation = movementPresentation(item.movementType)
            return <li key={item.stockMovementId} className="dashboard-movement-row">
              <div className="dashboard-movement-heading"><strong>{item.productBarcode}</strong><span className={`dashboard-movement-quantity ${presentation.className}`}>{presentation.sign}{numberFormatter.format(item.quantity)}</span></div>
              <div className="dashboard-movement-meta"><span>Depo: {item.warehouseCode}</span><span className={`dashboard-movement-type ${presentation.className}`}>{presentation.label}</span></div>
              <div className="dashboard-movement-footer"><span className="dashboard-movement-date">{formatMovementDate(item.createdAtUtc)}</span><span>Önceki: {numberFormatter.format(item.quantityBefore)} → Sonraki: {numberFormatter.format(item.quantityAfter)}</span></div>
            </li>
          })}</ul>
        ))}
        <div className="card-footnote"><ShieldCheck size={15} aria-hidden="true" />En fazla son 5 hareket gösterilir.</div>
      </Card>
      <Card title="Kritik Stoklar" icon={TriangleAlert} link={{ to: '/low-stock', label: 'İncele' }}>
        {criticalStock.status === 'loading' && <p className="dashboard-section-state" role="status">Kritik stoklar yükleniyor...</p>}
        {criticalStock.status === 'error' && <p className="dashboard-section-state is-error" role="alert">{criticalStock.message} Yenile ile tekrar deneyin.</p>}
        {criticalStock.status === 'success' && (criticalStock.items.length === 0 ? <EmptyState icon={TriangleAlert} title="Kayıtlı stoklarda kritik stok uyarısı bulunmuyor." description="Stok kaydı olmayan ürün-depo çiftleri bu sonuçların kapsamı dışındadır." compact /> : (
          <ul className="dashboard-critical-list">{criticalStock.items.slice(0, 5).map((item) => {
            const presentation = criticalPresentation(item.quantity, item.minimumStock)
            return <li key={`${item.productId}:${item.warehouseId}`} className="dashboard-critical-row">
              <div className="dashboard-critical-heading"><strong>{item.productBarcode}</strong><span className={`dashboard-critical-status ${presentation.className}`}>{presentation.label}</span></div>
              <p className="dashboard-critical-name">{item.productName}</p>
              <p className="dashboard-critical-warehouse">{item.warehouseName}</p>
              <dl className="dashboard-critical-quantities"><div><dt>Mevcut</dt><dd>{numberFormatter.format(item.quantity)}</dd></div><div><dt>Minimum</dt><dd>{numberFormatter.format(item.minimumStock)}</dd></div><div><dt>Eksik</dt><dd>{numberFormatter.format(item.shortage)}</dd></div></dl>
            </li>
          })}</ul>
        ))}
        <div className="stock-note"><span className="amber-dot" />Yalnızca kayıtlı stoklar değerlendirilir. En fazla 5 uyarı gösterilir.</div>
      </Card>
    </div>
    <section className="explore-section" aria-labelledby="explore-title"><div className="section-intro"><div><span className="eyebrow">HIZLI ERİŞİM</span><h2 id="explore-title">Çalışma alanınızı keşfedin</h2></div></div><div className="quick-links">
      <Link to="/products" className="quick-link"><span className="quick-icon"><Package size={22} aria-hidden="true" /></span><div><h3>Ürün kataloğu</h3><p>Tüm ürünleriniz için ortak alan.</p></div><ArrowUpRight size={19} aria-hidden="true" /></Link>
      <Link to="/warehouses" className="quick-link"><span className="quick-icon"><Building2 size={22} aria-hidden="true" /></span><div><h3>Depo yönetimi</h3><p>Envanterinizin bulunduğu yerler.</p></div><ArrowUpRight size={19} aria-hidden="true" /></Link>
      <Link to="/stock" className="quick-link"><span className="quick-icon"><ChartNoAxesCombined size={22} aria-hidden="true" /></span><div><h3>Stok çalışma alanı</h3><p>Giriş, çıkış ve stok görünümü.</p></div><ArrowUpRight size={19} aria-hidden="true" /></Link>
    </div></section>
  </>
}
