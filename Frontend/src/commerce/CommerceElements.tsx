import { Link } from 'react-router-dom'

export function CommerceError({ message, retry }: { message: string; retry?: () => void }) {
  return <div className="commerce-error" role="alert"><p>{message}</p>{retry && <button className="products-retry" onClick={retry}>Tekrar Dene</button>}</div>
}

export function PortalLinks() {
  return <nav className="commerce-tabs" aria-label="Müşteri alışveriş menüsü"><Link to="/portal/products">Ürünler</Link><Link to="/portal/cart">Sepetim</Link><Link to="/portal/orders">Siparişlerim</Link></nav>
}

export function Pagination({ page, size, total, busy, change }: { page: number; size: number; total: number; busy?: boolean; change: (page: number) => void }) {
  return <div className="commerce-pagination"><button className="product-cancel" disabled={busy || page <= 1} onClick={() => change(page - 1)}>Önceki</button><span>Sayfa {page} · {total} kayıt</span><button className="product-cancel" disabled={busy || page * size >= total} onClick={() => change(page + 1)}>Sonraki</button></div>
}
