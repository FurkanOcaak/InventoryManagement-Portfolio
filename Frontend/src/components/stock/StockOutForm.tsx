import { apiFetch } from '../../api/apiFetch'
import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'

interface StockOption {
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

type StockOptionsState = { status: 'loading' } | { status: 'error'; message: string } | { status: 'success'; items: StockOption[] }
type Field = 'inventoryId' | 'quantity' | 'note'
type FieldErrors = Partial<Record<Field, string>>
const fieldNames: Field[] = ['inventoryId', 'quantity', 'note']
const quantityFormatter = new Intl.NumberFormat('tr-TR')
const uncertainMessage = 'Stok çıkışının kaydedilip kaydedilmediği doğrulanamadı. Tekrar denemeden önce güncel stok listesini kontrol edin.'

export function StockOutForm({ onSuccess, onCancel }: { onSuccess: () => void; onCancel: () => void }) {
  const [stock, setStock] = useState<StockOptionsState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)
  const [values, setValues] = useState<Record<Field, string>>({ inventoryId: '', quantity: '', note: '' })
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
      // Do not treat aborting a browser POST as rolling back the server's stock operation.
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    async function loadStock() {
      try {
        const response = await apiFetch('/api/stock', { signal: controller.signal })
        if (!response.ok) throw new Error(`Stok kayıtları yüklenemedi (HTTP ${response.status}).`)
        const items: StockOption[] = await response.json()
        if (!Array.isArray(items)) throw new Error('Sunucudan geçerli bir stok listesi alınamadı.')
        if (!controller.signal.aborted) setStock({ status: 'success', items: items.filter((item) => item.quantity > 0) })
      } catch (error: unknown) {
        if (!controller.signal.aborted) setStock({ status: 'error', message: error instanceof Error && !(error instanceof TypeError) && !(error instanceof SyntaxError) ? error.message : 'Stok kayıtları alınamadı. Bağlantınızı kontrol edip tekrar deneyin.' })
      }
    }
    void loadStock()
    return () => controller.abort()
  }, [attempt])

  function retryStock() {
    if (submittingRef.current) return
    setStock({ status: 'loading' })
    setAttempt((previous) => previous + 1)
  }

  const selectedStock = stock.status === 'success' ? stock.items.find((item) => item.inventoryId === values.inventoryId) : undefined

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submittingRef.current) return
    const nextErrors: FieldErrors = {}
    if (!selectedStock) nextErrors.inventoryId = 'Çıkış yapılabilecek bir ürün / depo kaydı seçin.'
    const quantityText = values.quantity.trim()
    const quantity = Number(quantityText)
    const note = values.note.trim()
    if (!/^\d+$/.test(quantityText) || !Number.isSafeInteger(quantity) || quantity <= 0 || quantity > 2147483647) {
      nextErrors.quantity = '1 ile 2.147.483.647 arasında bir tam sayı girin.'
    } else if (selectedStock && quantity > selectedStock.quantity) {
      nextErrors.quantity = `Yetersiz stok. Mevcut miktar: ${quantityFormatter.format(selectedStock.quantity)} adet.`
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
    if (!selectedStock) return

    submittingRef.current = true
    setSaving(true)
    try {
      const response = await apiFetch('/api/stock/out', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ productId: selectedStock.productId, warehouseId: selectedStock.warehouseId, quantity, note: note || null }),
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
            const property = key.split('.').pop()?.toLowerCase()
            const field = property === 'productid' || property === 'warehouseid' ? 'inventoryId' : fieldNames.find((item) => item.toLowerCase() === property)
            if (field) backendErrors[field] = [backendErrors[field], text].filter(Boolean).join(' ')
            else otherErrors.push(text)
          }
        }
        setErrors(backendErrors)
        if (otherErrors.length || !Object.keys(backendErrors).length) setGeneralError(otherErrors.join(' ') || 'Stok çıkışı bilgileri kabul edilmedi. Alanları kontrol edin.')
      } else if (response.status === 404) {
        setGeneralError('Seçilen ürün veya depo artık mevcut ya da aktif olmayabilir. Stok listesini yenileyip seçiminizi kontrol edin.')
      } else if (response.status === 409) {
        setGeneralError('Yetersiz stok veya stok işlemi çakışması oluştu. Stok listesini yenileyip güncel miktarı kontrol edin. Otomatik tekrar yapılmadı.')
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
    <form ref={formRef} id="stock-out-form" className="product-create-form stock-in-form stock-out-form" onSubmit={submit} noValidate aria-labelledby="stock-out-title" aria-busy={saving}>
      <div className="product-form-heading"><h2 id="stock-out-title">Stok Çıkışı</h2><p>Yıldız (*) ile işaretli alanlar zorunludur. Çıkış yapılacak ürün ve depoyu stok listesinden seçin.</p></div>
      {generalError && <p className="product-form-error" role="alert">{generalError}</p>}
      {saving && <p className="stock-in-progress" role="status">Kaydediliyor... İşlem tamamlanana kadar bu sayfada bekleyin.</p>}
      <fieldset disabled={saving}>
        <legend className="product-form-sr-only">Stok çıkışı bilgileri</legend>
        <div className="product-form-grid">
          <div className="product-form-field stock-in-wide">
            <label htmlFor="stock-out-selection">Ürün / Depo <span aria-hidden="true">*</span></label>
            <select id="stock-out-selection" name="inventoryId" required disabled={stock.status !== 'success' || stock.items.length === 0} value={values.inventoryId} onChange={(event) => setValues({ ...values, inventoryId: event.target.value })} aria-invalid={!!errors.inventoryId} aria-describedby="stock-out-selection-info stock-out-selection-error">
              <option value="">Ürün ve depo seçin</option>
              {stock.status === 'success' && stock.items.map((item) => <option key={item.inventoryId} value={item.inventoryId}>{item.productBarcode} — {item.productName} | {item.warehouseCode} — {item.warehouseName} | {quantityFormatter.format(item.quantity)} adet</option>)}
            </select>
            <div id="stock-out-selection-info" className="product-form-hint">
              {stock.status === 'loading' && <p role="status">Stok kayıtları yükleniyor...</p>}
              {stock.status === 'error' && <p role="alert">{stock.message}</p>}
              {stock.status === 'success' && stock.items.length === 0 && <p>Çıkış yapılabilecek stok bulunmuyor. Mevcut stok kayıtları boş veya tüm miktarlar sıfır.</p>}
              {stock.status === 'success' && values.inventoryId && !selectedStock && <p>Seçilen kayıt artık çıkış için uygun değil. Mevcut listeden tekrar seçim yapın.</p>}
            </div>
            <p id="stock-out-selection-error" className="product-field-error" role={errors.inventoryId ? 'alert' : undefined}>{errors.inventoryId}</p>
          </div>
          {selectedStock && <p className="stock-out-current stock-in-wide" role="status">Seçilen ürün / depo için mevcut stok: <strong>{quantityFormatter.format(selectedStock.quantity)} adet</strong></p>}
          <div className="product-form-field">
            <label htmlFor="stock-out-quantity">Çıkış Miktarı <span aria-hidden="true">*</span></label>
            <input id="stock-out-quantity" name="quantity" type="text" inputMode="numeric" required value={values.quantity} onChange={(event) => setValues({ ...values, quantity: event.target.value })} aria-invalid={!!errors.quantity} aria-describedby={errors.quantity ? 'stock-out-quantity-error' : undefined} />
            {errors.quantity && <p id="stock-out-quantity-error" className="product-field-error" role="alert">{errors.quantity}</p>}
          </div>
          <div className="product-form-field stock-in-wide">
            <label htmlFor="stock-out-note">Açıklama</label>
            <textarea id="stock-out-note" name="note" rows={3} maxLength={500} value={values.note} onChange={(event) => setValues({ ...values, note: event.target.value })} aria-invalid={!!errors.note} aria-describedby="stock-out-note-hint stock-out-note-error" />
            <p id="stock-out-note-hint" className="product-form-hint">En fazla 500 karakter.</p>
            <p id="stock-out-note-error" className="product-field-error" role={errors.note ? 'alert' : undefined}>{errors.note}</p>
          </div>
        </div>
        <div className="stock-in-options-actions"><button type="button" className="product-cancel" onClick={retryStock}>{stock.status === 'error' ? 'Tekrar Dene' : 'Stok Listesini Yenile'}</button></div>
        <div className="product-form-actions"><button type="button" className="product-cancel" onClick={() => { if (!submittingRef.current) onCancel() }}>İptal</button><button type="submit" className="stock-retry" disabled={!selectedStock}>{saving ? 'Kaydediliyor...' : 'Kaydet'}</button></div>
      </fieldset>
    </form>
  )
}
