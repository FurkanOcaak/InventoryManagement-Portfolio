import { apiFetch } from '../../api/apiFetch'
import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'

interface ProductOption { id: string; barcode: string; name: string; isActive: boolean }
interface WarehouseOption { id: string; code: string; name: string; isActive: boolean }
type OptionsState<T> = { status: 'loading' } | { status: 'error'; message: string } | { status: 'success'; items: T[] }
type Field = 'productId' | 'warehouseId' | 'quantity' | 'note'
type FieldErrors = Partial<Record<Field, string>>
const fieldNames: Field[] = ['productId', 'warehouseId', 'quantity', 'note']
const uncertainMessage = 'Stok girişinin kaydedilip kaydedilmediği doğrulanamadı. Tekrar denemeden önce stok listesini kontrol edin.'

async function fetchOptions<T extends { isActive: boolean }>(url: string, label: string, signal: AbortSignal): Promise<T[]> {
  const response = await apiFetch(url, { signal })
  if (!response.ok) throw new Error(`${label} yüklenemedi (HTTP ${response.status}).`)
  const items: T[] = await response.json()
  if (!Array.isArray(items)) throw new Error(`${label} için geçerli bir liste alınamadı.`)
  return items.filter((item) => item.isActive)
}

export function StockInForm({ onSuccess, onCancel }: { onSuccess: () => void; onCancel: () => void }) {
  const [products, setProducts] = useState<OptionsState<ProductOption>>({ status: 'loading' })
  const [warehouses, setWarehouses] = useState<OptionsState<WarehouseOption>>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)
  const [values, setValues] = useState<Record<Field, string>>({ productId: '', warehouseId: '', quantity: '', note: '' })
  const [errors, setErrors] = useState<FieldErrors>({})
  const [generalError, setGeneralError] = useState('')
  const [saving, setSaving] = useState(false)
  const submittingRef = useRef(false)
  const mountedRef = useRef(false)
  const formRef = useRef<HTMLFormElement>(null)

  useEffect(() => {
    mountedRef.current = true
    formRef.current?.querySelector<HTMLSelectElement>('select')?.focus()
    const preventLeaving = (event: BeforeUnloadEvent) => {
      if (submittingRef.current) { event.preventDefault(); event.returnValue = '' }
    }
    const preventNavigation = (event: MouseEvent) => {
      if (submittingRef.current && event.target instanceof Element && event.target.closest('a[href]')) {
        event.preventDefault()
        event.stopImmediatePropagation()
      }
    }
    window.addEventListener('beforeunload', preventLeaving)
    document.addEventListener('click', preventNavigation, true)
    return () => {
      mountedRef.current = false
      window.removeEventListener('beforeunload', preventLeaving)
      document.removeEventListener('click', preventNavigation, true)
      // A pending POST is not aborted: cancelling a browser request cannot undo a committed stock operation.
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    async function loadProducts() {
      try {
        const items = await fetchOptions<ProductOption>('/api/products', 'Ürünler', controller.signal)
        if (!controller.signal.aborted) setProducts({ status: 'success', items })
      } catch (error: unknown) {
        if (!controller.signal.aborted) setProducts({ status: 'error', message: error instanceof Error && !(error instanceof TypeError) && !(error instanceof SyntaxError) ? error.message : 'Ürünler alınamadı. Bağlantınızı kontrol edip tekrar deneyin.' })
      }
    }
    async function loadWarehouses() {
      try {
        const items = await fetchOptions<WarehouseOption>('/api/warehouses', 'Depolar', controller.signal)
        if (!controller.signal.aborted) setWarehouses({ status: 'success', items })
      } catch (error: unknown) {
        if (!controller.signal.aborted) setWarehouses({ status: 'error', message: error instanceof Error && !(error instanceof TypeError) && !(error instanceof SyntaxError) ? error.message : 'Depolar alınamadı. Bağlantınızı kontrol edip tekrar deneyin.' })
      }
    }
    void loadProducts()
    void loadWarehouses()
    return () => controller.abort()
  }, [attempt])

  function retryOptions() {
    if (submittingRef.current) return
    setProducts({ status: 'loading' })
    setWarehouses({ status: 'loading' })
    setAttempt((previous) => previous + 1)
  }

  const productSelected = products.status === 'success' && products.items.some((item) => item.id === values.productId)
  const warehouseSelected = warehouses.status === 'success' && warehouses.items.some((item) => item.id === values.warehouseId)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submittingRef.current) return
    const nextErrors: FieldErrors = {}
    if (!productSelected) nextErrors.productId = 'Listeden aktif bir ürün seçin.'
    if (!warehouseSelected) nextErrors.warehouseId = 'Listeden aktif bir depo seçin.'
    const quantityText = values.quantity.trim()
    const quantity = Number(quantityText)
    const note = values.note.trim()
    if (!/^\d+$/.test(quantityText) || !Number.isSafeInteger(quantity) || quantity <= 0 || quantity > 2147483647) {
      nextErrors.quantity = '1 ile 2.147.483.647 arasında bir tam sayı girin.'
    }
    if (values.note.length > 500) nextErrors.note = 'Açıklama en fazla 500 karakter olabilir.'
    setErrors(nextErrors)
    setGeneralError('')
    const firstInvalid = fieldNames.find((field) => nextErrors[field])
    if (firstInvalid) {
      const input = formRef.current?.elements.namedItem(firstInvalid)
      if (input instanceof HTMLElement) input.focus()
      return
    }

    submittingRef.current = true
    setSaving(true)
    try {
      const response = await apiFetch('/api/stock/in', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ productId: values.productId, warehouseId: values.warehouseId, quantity, note: note || null }),
      })
      if (!mountedRef.current) return
      if (response.status === 201) {
        onSuccess()
        return
      }
      if (response.status === 400) {
        const problem: unknown = await response.json().catch(() => null)
        if (!mountedRef.current) return
        const backendErrors: FieldErrors = {}
        const otherErrors: string[] = []
        if (problem && typeof problem === 'object' && 'errors' in problem && problem.errors && typeof problem.errors === 'object') {
          for (const [key, messages] of Object.entries(problem.errors)) {
            const text = typeof messages === 'string' ? messages : Array.isArray(messages) ? messages.filter((message): message is string => typeof message === 'string').join(' ') : ''
            if (!text) continue
            const field = fieldNames.find((item) => item.toLowerCase() === key.split('.').pop()?.toLowerCase())
            if (field) backendErrors[field] = [backendErrors[field], text].filter(Boolean).join(' ')
            else otherErrors.push(text)
          }
        }
        setErrors(backendErrors)
        if (otherErrors.length || !Object.keys(backendErrors).length) setGeneralError(otherErrors.join(' ') || 'Stok girişi bilgileri kabul edilmedi. Alanları kontrol edin.')
      } else if (response.status === 404) {
        setGeneralError('Seçilen ürün veya depo artık mevcut ya da aktif olmayabilir. Ürün ve depo listelerini yenileyip seçimlerinizi kontrol edin.')
      } else if (response.status === 409) {
        setGeneralError('Stok işleminde çakışma yaşandı. Tekrar denemeden önce miktarı ve güncel stok listesini kontrol edin.')
      } else {
        setGeneralError(`${uncertainMessage} (HTTP ${response.status})`)
      }
    } catch {
      if (mountedRef.current) setGeneralError(uncertainMessage)
    } finally {
      submittingRef.current = false
      if (mountedRef.current) setSaving(false)
    }
  }

  return (
    <form ref={formRef} id="stock-in-form" className="product-create-form stock-in-form" onSubmit={submit} noValidate aria-labelledby="stock-in-title" aria-busy={saving}>
      <div className="product-form-heading"><h2 id="stock-in-title">Stok Girişi</h2><p>Yıldız (*) ile işaretli alanlar zorunludur. Aktif ürün ve depo seçerek giriş miktarını belirtin.</p></div>
      {generalError && <p className="product-form-error" role="alert">{generalError}</p>}
      {saving && <p className="stock-in-progress" role="status">Kaydediliyor... İşlem tamamlanana kadar bu sayfada bekleyin.</p>}
      <fieldset disabled={saving}>
        <legend className="product-form-sr-only">Stok girişi bilgileri</legend>
        <div className="product-form-grid">
          <div className="product-form-field">
            <label htmlFor="stock-in-product">Ürün <span aria-hidden="true">*</span></label>
            <select id="stock-in-product" name="productId" required disabled={products.status !== 'success' || products.items.length === 0} value={values.productId} onChange={(event) => setValues({ ...values, productId: event.target.value })} aria-invalid={!!errors.productId} aria-describedby="stock-in-product-info stock-in-product-error">
              <option value="">Ürün seçin</option>
              {products.status === 'success' && products.items.map((item) => <option key={item.id} value={item.id}>{item.barcode} — {item.name}</option>)}
            </select>
            <div id="stock-in-product-info" className="product-form-hint">
              {products.status === 'loading' && <p role="status">Ürünler yükleniyor...</p>}
              {products.status === 'error' && <p role="alert">{products.message}</p>}
              {products.status === 'success' && products.items.length === 0 && <p>Aktif ürün bulunmuyor. Stok girişi için önce aktif bir ürün bulunmalıdır.</p>}
            </div>
            <p id="stock-in-product-error" className="product-field-error" role={errors.productId ? 'alert' : undefined}>{errors.productId}</p>
          </div>
          <div className="product-form-field">
            <label htmlFor="stock-in-warehouse">Depo <span aria-hidden="true">*</span></label>
            <select id="stock-in-warehouse" name="warehouseId" required disabled={warehouses.status !== 'success' || warehouses.items.length === 0} value={values.warehouseId} onChange={(event) => setValues({ ...values, warehouseId: event.target.value })} aria-invalid={!!errors.warehouseId} aria-describedby="stock-in-warehouse-info stock-in-warehouse-error">
              <option value="">Depo seçin</option>
              {warehouses.status === 'success' && warehouses.items.map((item) => <option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
            </select>
            <div id="stock-in-warehouse-info" className="product-form-hint">
              {warehouses.status === 'loading' && <p role="status">Depolar yükleniyor...</p>}
              {warehouses.status === 'error' && <p role="alert">{warehouses.message}</p>}
              {warehouses.status === 'success' && warehouses.items.length === 0 && <p>Aktif depo bulunmuyor. Stok girişi için önce aktif bir depo bulunmalıdır.</p>}
            </div>
            <p id="stock-in-warehouse-error" className="product-field-error" role={errors.warehouseId ? 'alert' : undefined}>{errors.warehouseId}</p>
          </div>
          <div className="product-form-field">
            <label htmlFor="stock-in-quantity">Giriş Miktarı <span aria-hidden="true">*</span></label>
            <input id="stock-in-quantity" name="quantity" type="text" inputMode="numeric" required value={values.quantity} onChange={(event) => setValues({ ...values, quantity: event.target.value })} aria-invalid={!!errors.quantity} aria-describedby={errors.quantity ? 'stock-in-quantity-error' : undefined} />
            {errors.quantity && <p id="stock-in-quantity-error" className="product-field-error" role="alert">{errors.quantity}</p>}
          </div>
          <div className="product-form-field stock-in-wide">
            <label htmlFor="stock-in-note">Açıklama</label>
            <textarea id="stock-in-note" name="note" rows={3} maxLength={500} value={values.note} onChange={(event) => setValues({ ...values, note: event.target.value })} aria-invalid={!!errors.note} aria-describedby="stock-in-note-hint stock-in-note-error" />
            <p id="stock-in-note-hint" className="product-form-hint">En fazla 500 karakter.</p>
            <p id="stock-in-note-error" className="product-field-error" role={errors.note ? 'alert' : undefined}>{errors.note}</p>
          </div>
        </div>
        <div className="stock-in-options-actions"><button type="button" className="product-cancel" onClick={retryOptions}>Ürün ve Depo Listelerini Yenile</button></div>
        <div className="product-form-actions"><button type="button" className="product-cancel" onClick={() => { if (!submittingRef.current) onCancel() }}>İptal</button><button type="submit" className="stock-retry" disabled={!productSelected || !warehouseSelected}>{saving ? 'Kaydediliyor...' : 'Kaydet'}</button></div>
      </fieldset>
    </form>
  )
}
