import { useEffect, useState } from 'react'
import { apiFetch, ApiError } from '../api/apiFetch'
import { PortalLinks } from '../commerce/CommerceElements'
import '../styles/commerce.css'

export function CustomerPortalPage() {
  const [name, setName] = useState('')
  const [error, setError] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    void apiFetch('/api/customer-portal/me', { signal: controller.signal }).then(async (response) => {
      if (!response.ok) throw new Error('Müşteri hesabınız doğrulanamadı. Hesap bağlantısı için yöneticinizle görüşün.')
      const data: unknown = await response.json()
      if (!data || typeof data !== 'object' || !('name' in data) || typeof data.name !== 'string') throw new Error('Müşteri bilgisi alınamadı.')
      if (!controller.signal.aborted) setName(data.name)
    }).catch((failure: unknown) => {
      if (!controller.signal.aborted) setError(failure instanceof ApiError ? 'Müşteri hesabınız doğrulanamadı. Hesap bağlantısı için yöneticinizle görüşün.' : failure instanceof Error ? failure.message : 'Müşteri bilgisi alınamadı.')
    })
    return () => controller.abort()
  }, [])
  return <section className="auth-denied" aria-labelledby="portal-title"><p className="eyebrow">MÜŞTERİ ALANI</p><h1 id="portal-title">Müşteri portalı</h1>
    {error ? <p role="alert">{error}</p> : name ? <p>Hoş geldiniz, {name}.</p> : <p role="status">Müşteri hesabınız doğrulanıyor...</p>}
    <p>Ürün kataloğunu inceleyin, sepetinizi hazırlayın ve siparişlerinizi takip edin.</p>
    {!error && name && <PortalLinks />}
  </section>
}
