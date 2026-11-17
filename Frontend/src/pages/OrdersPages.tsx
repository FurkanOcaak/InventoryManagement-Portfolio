import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { commerceRequest, money, orderDate, stockNotice } from '../commerce/commerceApi'
import type { Page, OrderSummary, Order, AdminOrderSummary, AdminOrder } from '../commerce/commerceApi'
import { CommerceError, Pagination, PortalLinks } from '../commerce/CommerceElements'
import { PageHeading } from '../components/ui/PageElements'
import '../styles/commerce.css'

export function OrdersPage({ admin = false }: { admin?: boolean }) {
  const [page, setPage] = useState(1)
  const [revision, setRevision] = useState(0)
  const [rows, setRows] = useState<{ order: OrderSummary; customer?: string }[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const prefix = admin ? '/orders' : '/portal/orders'
  useEffect(() => {
    const controller = new AbortController()
    const endpoint = admin ? '/api/orders' : '/api/customer-portal/orders'
    void commerceRequest<Page<OrderSummary | AdminOrderSummary>>(`${endpoint}?page=${page}&pageSize=20`, { signal: controller.signal }).then((value) => {
      if (controller.signal.aborted) return
      setRows(value.items.map((item) => 'order' in item ? { order: item.order, customer: `${item.customerCode} — ${item.customerName}` } : { order: item })); setTotal(value.totalCount)
    }).catch((failure: unknown) => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : 'Siparişler yüklenemedi.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [admin, page, revision])
  return <section className="commerce-page"><PageHeading eyebrow={admin ? 'SİPARİŞ YÖNETİMİ' : 'MÜŞTERİ ALANI'} title={admin ? 'Tüm siparişler' : 'Siparişlerim'} description="Siparişlerinizin kalemlerini ve onay bekleyen durumunu görüntüleyin." />{!admin && <PortalLinks />}
    {error ? <CommerceError message={error} retry={() => { setLoading(true); setError(''); setRevision((value) => value + 1) }} /> : loading ? <p role="status">Siparişler yükleniyor...</p> : <>
      {rows.length === 0 ? <div className="commerce-empty">Henüz sipariş bulunmuyor.{!admin && <> <Link to="/portal/products">Ürünleri keşfedin</Link></>}</div> : <div className="commerce-table-scroll"><table className="commerce-table"><caption>{admin ? 'Bütün müşteri siparişleri' : 'Sipariş geçmişi'}</caption><thead><tr><th>Sipariş numarası</th>{admin && <th>Müşteri</th>}<th>Tarih</th><th>Durum</th><th>Toplam</th></tr></thead><tbody>{rows.map(({ order, customer }) => <tr key={order.id}><td><Link to={`${prefix}/${order.id}`}>{order.number}</Link></td>{admin && <td>{customer}</td>}<td>{orderDate(order.createdAtUtc)}</td><td><span className="commerce-pending">Onay bekliyor</span></td><td>{money(order.total)}</td></tr>)}</tbody></table></div>}
      <Pagination page={page} size={20} total={total} change={(next) => { setLoading(true); setError(''); setPage(next) }} />
    </>}
  </section>
}

export function OrderDetailPage({ admin = false }: { admin?: boolean }) {
  const { id } = useParams()
  return <OrderDetailContent key={`${admin}-${id}`} admin={admin} id={id ?? ''} />
}

function OrderDetailContent({ admin, id }: { admin: boolean; id: string }) {
  const [order, setOrder] = useState<Order | null>(null)
  const [customer, setCustomer] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [revision, setRevision] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    void commerceRequest<Order | AdminOrder>(`${admin ? '/api/orders' : '/api/customer-portal/orders'}/${encodeURIComponent(id ?? '')}`, { signal: controller.signal }).then((value) => {
      if (controller.signal.aborted) return
      if ('order' in value) { setOrder(value.order); setCustomer(`${value.customerCode} — ${value.customerName}`) } else setOrder(value)
    }).catch((failure: unknown) => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : 'Sipariş yüklenemedi.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [admin, id, revision])
  return <section className="commerce-page"><PageHeading eyebrow="SİPARİŞ DETAYI" title="Sipariş bilgileri" description="Sipariş anında kaydedilen fiyatlar ve kalemler." />{!admin && <PortalLinks />}<Link className="text-link" to={admin ? '/orders' : '/portal/orders'}>Siparişlere dön</Link>
    {error ? <CommerceError message={error} retry={() => { setLoading(true); setError(''); setRevision((value) => value + 1) }} /> : loading ? <p role="status">Sipariş yükleniyor...</p> : order && <>
      <div className="commerce-order-header"><h2>{order.number}</h2><span className="commerce-pending">Onay bekliyor</span><p>{orderDate(order.createdAtUtc)}</p>{customer && <p>{customer}</p>}</div>
      <div className="commerce-table-scroll"><table className="commerce-table"><caption>Sipariş kalemleri</caption><thead><tr><th>Ürün</th><th>Barkod</th><th>Adet</th><th>Birim fiyat</th><th>Ara toplam</th></tr></thead><tbody>{order.items.map((item) => <tr key={item.productId}><td>{item.name}</td><td>{item.barcode}</td><td>{item.quantity}</td><td>{money(item.unitPrice)}</td><td>{money(item.subtotal)}</td></tr>)}</tbody></table></div>
      <div className="commerce-checkout"><h2>Toplam <strong>{money(order.total)}</strong></h2><p>{stockNotice}</p></div>
    </>}
  </section>
}
