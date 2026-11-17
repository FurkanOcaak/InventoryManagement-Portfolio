import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { checkoutAttempt, commerceRequest, CommerceRequestError, finishCheckout, jsonRequest, money, pendingCheckout, quantityValue, stockNotice } from '../commerce/commerceApi'
import type { Cart, Order } from '../commerce/commerceApi'
import { CommerceError, PortalLinks } from '../commerce/CommerceElements'
import { PageHeading } from '../components/ui/PageElements'
import '../styles/commerce.css'

export function PortalCartPage() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [cart, setCart] = useState<Cart | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [confirmed, setConfirmed] = useState(false)
  const [pendingOrder, setPendingOrder] = useState(() => { try { return !!user && !!pendingCheckout(sessionStorage, user.id) } catch { return false } })
  const [revision, setRevision] = useState(0)
  const [quantities, setQuantities] = useState<Record<string, string>>({})
  const pending = useRef(false)
  const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  useEffect(() => {
    const controller = new AbortController()
    void commerceRequest<Cart>('/api/customer-portal/cart', { signal: controller.signal }).then((value) => {
      if (controller.signal.aborted) return
      setCart(value); setQuantities(Object.fromEntries(value.items.map((item) => [item.productId, String(item.quantity)])))
    }).catch((failure: unknown) => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : 'Sepet yüklenemedi.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [revision])

  async function change(productId: string, remove = false) {
    if (pending.current || !cart) return
    const quantity = quantityValue(quantities[productId] ?? '')
    if (!remove && quantity === null) { setError('Adet 1–10000 arasında bir tam sayı olmalı.'); return }
    pending.current = true; setBusy(true); setError('')
    try {
      const next = await commerceRequest<Cart>(`/api/customer-portal/cart/items/${productId}${remove ? `?version=${cart.version}` : ''}`, remove ? { method: 'DELETE' } : jsonRequest('PATCH', { quantity, cartVersion: cart.version }))
      if (mounted.current) { setCart(next); setConfirmed(false); setQuantities(Object.fromEntries(next.items.map((item) => [item.productId, String(item.quantity)]))) }
    } catch (failure) { if (mounted.current) setError(failure instanceof Error ? failure.message : 'Sepet güncellenemedi. Güncel sepeti yükleyin.') }
    finally { pending.current = false; if (mounted.current) setBusy(false) }
  }

  async function checkout() {
    if (pending.current || !cart || !user || !confirmed) return
    pending.current = true; setBusy(true); setError('')
    try {
      const attempt = checkoutAttempt(sessionStorage, user.id, cart.version)
      setPendingOrder(true)
      const order = await commerceRequest<Order>('/api/customer-portal/orders', jsonRequest('POST', attempt))
      finishCheckout(sessionStorage, user.id)
      if (mounted.current) navigate(`/portal/orders/${order.id}`, { replace: true })
    } catch (failure) {
      if (failure instanceof CommerceRequestError && [400, 409].includes(failure.status)) {
        finishCheckout(sessionStorage, user.id); if (mounted.current) setPendingOrder(false)
      }
      if (mounted.current) setError(failure instanceof Error ? failure.message : 'Sipariş sonucu alınamadı. Sipariş geçmişinizi kontrol edin veya aynı isteğin sonucunu yeniden sorgulayın.')
    } finally { pending.current = false; if (mounted.current) setBusy(false) }
  }

  return <section className="commerce-page"><PageHeading eyebrow="MÜŞTERİ ALANI" title="Sepetim" description="Miktarlarınızı düzenleyin ve siparişinizi kontrol edin." /><PortalLinks />
    {error && <CommerceError message={error} retry={() => { setLoading(true); setError(''); setConfirmed(false); setRevision((value) => value + 1) }} />}
    {loading ? <p role="status">Sepet yükleniyor...</p> : cart && <>
      {cart.items.length === 0 ? <div className="commerce-empty">Sepetiniz boş. <Link to="/portal/products">Ürünleri keşfedin</Link></div> : <div className="commerce-table-scroll"><table className="commerce-table"><caption>Sepet ürünleri</caption><thead><tr><th>Ürün</th><th>Birim fiyat</th><th>Adet</th><th>Ara toplam</th><th>İşlem</th></tr></thead><tbody>{cart.items.map((item) => <tr key={item.productId}>
        <td><strong>{item.name}</strong><span className="commerce-muted">{item.barcode}</span>{!item.available && <span role="alert">Ürün artık satışta değil; sepetten çıkarın.</span>}</td><td>{money(item.unitPrice)}</td>
        <td><label className="product-form-sr-only" htmlFor={`cart-${item.productId}`}>{item.name} adedi</label><input id={`cart-${item.productId}`} type="text" inputMode="numeric" maxLength={5} disabled={busy || pendingOrder} value={quantities[item.productId] ?? String(item.quantity)} onChange={(event) => { setQuantities((value) => ({ ...value, [item.productId]: event.target.value })); setConfirmed(false) }} /></td>
        <td>{money(item.subtotal)}</td><td><div className="commerce-row-actions"><button className="product-cancel" disabled={busy || pendingOrder || !item.available} onClick={() => void change(item.productId)}>Güncelle</button><button className="product-cancel" disabled={busy || pendingOrder} onClick={() => void change(item.productId, true)}>Çıkar</button></div></td>
      </tr>)}</tbody></table></div>}
      {(cart.items.length > 0 || pendingOrder) && <aside className="commerce-checkout"><h2>Genel toplam <strong>{money(cart.total)}</strong></h2><p>{stockNotice}</p><p>Fiyat ve stok, sipariş anında sunucuda yeniden kontrol edilir. Fiyat değişiklikleri sipariş toplamına yansıyabilir.</p>
        {pendingOrder && <p role="status">Önceki sipariş isteğinin sonucu bekleniyor. Aynı isteğin sonucunu kontrol edebilir veya <Link to="/portal/orders">sipariş geçmişinize</Link> bakabilirsiniz.</p>}
        {!pendingOrder && cart.items.some((item) => quantityValue(quantities[item.productId] ?? '') !== item.quantity) && <p role="status">Siparişten önce adet değişikliklerini Güncelle düğmesiyle kaydedin.</p>}
        <label className="commerce-confirm"><input type="checkbox" checked={confirmed} disabled={busy} onChange={(event) => setConfirmed(event.target.checked)} />Sipariş ve stok koşullarını okudum; siparişimi onaylıyorum.</label>
        <button className="products-retry" disabled={busy || !confirmed || (!pendingOrder && (cart.items.length === 0 || cart.items.some((item) => !item.available || quantityValue(quantities[item.productId] ?? '') !== item.quantity)))} onClick={() => void checkout()}>{busy ? 'Sipariş kontrol ediliyor...' : pendingOrder ? 'Önceki Siparişin Sonucunu Kontrol Et' : 'Siparişi Onayla'}</button>
      </aside>}
    </>}
  </section>
}
