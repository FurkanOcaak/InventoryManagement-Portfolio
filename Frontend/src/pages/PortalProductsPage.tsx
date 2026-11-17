import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { commerceRequest, jsonRequest, money, quantityValue } from '../commerce/commerceApi'
import type { Page, CatalogProduct, Cart } from '../commerce/commerceApi'
import { CommerceError, Pagination, PortalLinks } from '../commerce/CommerceElements'
import { PageHeading } from '../components/ui/PageElements'
import '../styles/commerce.css'

export function PortalProductsPage() {
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [revision, setRevision] = useState(0)
  const [data, setData] = useState<Page<CatalogProduct> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [quantities, setQuantities] = useState<Record<string, string>>({})
  const pending = useRef(false)
  const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  useEffect(() => {
    const controller = new AbortController()
    void commerceRequest<Page<CatalogProduct>>(`/api/customer-portal/products?search=${encodeURIComponent(query)}&page=${page}&pageSize=12`, { signal: controller.signal })
      .then((value) => { if (!controller.signal.aborted) setData(value) })
      .catch((failure: unknown) => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : 'Ürünler yüklenemedi.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [query, page, revision])

  function reload() { setLoading(true); setError(''); setRevision((value) => value + 1) }
  function submit(event: FormEvent) { event.preventDefault(); setPage(1); setQuery(search.trim()); reload() }
  async function add(product: CatalogProduct) {
    if (pending.current) return
    const quantity = quantityValue(quantities[product.id] ?? '1')
    if (quantity === null) { setError('Adet 1–10000 arasında bir tam sayı olmalı.'); return }
    pending.current = true; setBusy(true); setError(''); setNotice('')
    try {
      const cart = await commerceRequest<Cart>('/api/customer-portal/cart')
      await commerceRequest<Cart>('/api/customer-portal/cart/items', jsonRequest('POST', { productId: product.id, quantity, cartVersion: cart.version }))
      if (mounted.current) setNotice(`${product.name} sepete eklendi.`)
    } catch (failure) { if (mounted.current) setError(failure instanceof Error ? failure.message : 'Sepet işlemi tamamlanamadı. Sepetinizi kontrol edin.') }
    finally { pending.current = false; if (mounted.current) setBusy(false) }
  }

  return <section className="commerce-page"><PageHeading eyebrow="MÜŞTERİ ALANI" title="Ürün kataloğu" description="Ürünleri inceleyin, güncel satış fiyatlarıyla sepetinizi hazırlayın." /><PortalLinks />
    <form className="commerce-search" onSubmit={submit}><label htmlFor="catalog-search">Ürün adı veya barkod</label><input id="catalog-search" type="search" maxLength={100} value={search} onChange={(event) => setSearch(event.target.value)} /><button className="products-retry" disabled={busy}>Ara</button></form>
    {notice && <p className="commerce-success" role="status">{notice} <Link to="/portal/cart">Sepetimi görüntüle</Link></p>}
    {error && <CommerceError message={error} retry={reload} />}
    {loading ? <p role="status">Ürünler yükleniyor...</p> : data && <>
      {data.items.length === 0 ? <div className="commerce-empty">Aramanıza uygun aktif ürün bulunamadı.</div> : <div className="commerce-products">{data.items.map((product) => <article className="commerce-product" key={product.id}>
        <span className="eyebrow">{product.barcode}</span><h2>{product.name}</h2><p>{product.description || 'Ürün açıklaması bulunmuyor.'}</p><strong className="commerce-price">{money(product.unitPrice)}</strong>
        <label htmlFor={`quantity-${product.id}`}>Adet</label><div className="commerce-product-actions"><input id={`quantity-${product.id}`} aria-label={`${product.name} adedi`} type="text" inputMode="numeric" maxLength={5} value={quantities[product.id] ?? '1'} disabled={busy} onChange={(event) => setQuantities((value) => ({ ...value, [product.id]: event.target.value }))} /><button className="products-retry" disabled={busy} onClick={() => void add(product)}>Sepete Ekle</button></div>
      </article>)}</div>}
      <Pagination page={page} size={12} total={data.totalCount} busy={loading || busy} change={(next) => { setLoading(true); setError(''); setPage(next) }} />
    </>}
  </section>
}
