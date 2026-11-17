import { apiFetch } from '../../api/apiFetch'
import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'

type Field = 'barcode' | 'name' | 'description' | 'unitPrice' | 'minimumStock'
type FieldErrors = Partial<Record<Field, string>>

const fields: { key: Field; label: string; maxLength?: number; required: boolean }[] = [
  { key: 'barcode', label: 'Ürün Barkodu', maxLength: 50, required: true },
  { key: 'name', label: 'Ürün Adı', maxLength: 200, required: true },
  { key: 'description', label: 'Açıklama', maxLength: 1000, required: false },
  { key: 'unitPrice', label: 'Birim Fiyat (₺)', required: true },
  { key: 'minimumStock', label: 'Minimum Stok', required: true },
]

interface EditableProduct {
  id: string
  barcode: string
  name: string
  description: string | null
  unitPrice: number
  minimumStock: number
}

export function ProductEditForm({ product, onSuccess, onCancel }: { product: EditableProduct; onSuccess: () => void; onCancel: () => void }) {
  const [values, setValues] = useState<Record<Field, string>>({ barcode: product.barcode, name: product.name, description: product.description ?? '', unitPrice: String(product.unitPrice).replace('.', ','), minimumStock: String(product.minimumStock) })
  const [errors, setErrors] = useState<FieldErrors>({})
  const [generalError, setGeneralError] = useState('')
  const [saving, setSaving] = useState(false)
  const requestRef = useRef<AbortController | null>(null)
  const submittingRef = useRef(false)
  const formRef = useRef<HTMLFormElement>(null)

  useEffect(() => {
    formRef.current?.scrollIntoView({ block: 'start' })
    formRef.current?.querySelector<HTMLInputElement>('[name="barcode"]')?.focus({ preventScroll: true })
    return () => requestRef.current?.abort()
  }, [])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submittingRef.current) return

    const nextErrors: FieldErrors = {}
    const barcode = values.barcode.trim()
    const name = values.name.trim()
    const description = values.description.trim()
    const priceText = values.unitPrice.trim()
    const stockText = values.minimumStock.trim()
    const unitPrice = Number(priceText.replace(',', '.'))
    const minimumStock = Number(stockText)

    if (!barcode) nextErrors.barcode = 'Ürün Barkodu zorunludur; yalnızca boşluk içeremez.'
    else if (barcode.length > 50) nextErrors.barcode = 'Ürün Barkodu en fazla 50 karakter olabilir.'
    if (!name) nextErrors.name = 'Ürün adı zorunludur; yalnızca boşluk içeremez.'
    else if (name.length > 200) nextErrors.name = 'Ürün adı en fazla 200 karakter olabilir.'
    if (description.length > 1000) nextErrors.description = 'Açıklama en fazla 1000 karakter olabilir.'
    if (!/^(?:\d+(?:[.,]\d+)?|[.,]\d+)$/.test(priceText) || !Number.isFinite(unitPrice) || unitPrice < 0) {
      nextErrors.unitPrice = 'Sıfır veya daha büyük geçerli bir fiyat girin (ör. 250,50). Binlik ayırıcı kullanmayın.'
    }
    if (!/^\d+$/.test(stockText) || !Number.isSafeInteger(minimumStock) || minimumStock < 0) {
      nextErrors.minimumStock = 'Minimum stok sıfır veya daha büyük bir tam sayı olmalıdır.'
    }

    setErrors(nextErrors)
    setGeneralError('')
    const firstInvalid = fields.find((field) => nextErrors[field.key])
    if (firstInvalid) {
      const input = formRef.current?.elements.namedItem(firstInvalid.key)
      if (input instanceof HTMLElement) input.focus()
      return
    }

    submittingRef.current = true
    setSaving(true)
    const controller = new AbortController()
    requestRef.current = controller
    try {
      const response = await apiFetch(`/api/products/${encodeURIComponent(product.id)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        signal: controller.signal,
        body: JSON.stringify({ barcode, name, description: description || null, unitPrice, minimumStock }),
      })
      if (controller.signal.aborted) return
      if (response.status === 200) {
        onSuccess()
        return
      }
      if (response.status === 404) {
        setGeneralError('Bu ürün artık bulunamıyor. Güncelleme yapılamadı. Formu kapatıp ürün listesini yenileyin.')
      } else if (response.status === 409) {
        setErrors({ barcode: 'Bu Ürün Barkodu başka bir ürün tarafından kullanılıyor. Farklı bir Ürün Barkodu girin.' })
      } else if (response.status === 400) {
        const problem: unknown = await response.json().catch(() => null)
        if (controller.signal.aborted) return
        const backendErrors: FieldErrors = {}
        const otherErrors: string[] = []
        if (problem && typeof problem === 'object' && 'errors' in problem && problem.errors && typeof problem.errors === 'object') {
          for (const [key, messages] of Object.entries(problem.errors)) {
            const text = typeof messages === 'string' ? messages : Array.isArray(messages)
              ? messages.filter((message): message is string => typeof message === 'string').join(' ')
              : ''
            if (!text) continue
            const field = fields.find((item) => item.key.toLowerCase() === key.split('.').pop()?.toLowerCase())
            if (field) backendErrors[field.key] = [backendErrors[field.key], text].filter(Boolean).join(' ')
            else otherErrors.push(text)
          }
        }
        setErrors(backendErrors)
        if (otherErrors.length || !Object.keys(backendErrors).length) {
          setGeneralError(otherErrors.join(' ') || 'Gönderilen ürün bilgileri kabul edilmedi. Alanları kontrol edip tekrar deneyin.')
        }
      } else if (response.status >= 500) {
        setGeneralError(`Sunucuda bir hata oluştu (HTTP ${response.status}). Güncelleme durumunu ürün listesinden kontrol ettikten sonra tekrar deneyin.`)
      } else {
        setGeneralError(`Ürün güncellenemedi (HTTP ${response.status}). Lütfen tekrar deneyin.`)
      }
    } catch {
      if (!controller.signal.aborted) {
        setGeneralError('Sunucuya ulaşılamadı veya yanıt alınamadı. Bağlantınızı ve ürün listesini kontrol ettikten sonra tekrar deneyin.')
      }
    } finally {
      if (!controller.signal.aborted) {
        submittingRef.current = false
        setSaving(false)
      }
    }
  }

  return (
    <form ref={formRef} id="product-edit-form" className="product-create-form product-edit-form" onSubmit={submit} noValidate aria-labelledby="product-edit-title" aria-busy={saving}>
      <div className="product-form-heading"><h2 id="product-edit-title">Ürünü Düzenle</h2><p>Yıldız (*) ile işaretli alanlar zorunludur. Bu işlem stok miktarını değiştirmez.</p></div>
      {generalError && <p className="product-form-error" role="alert">{generalError}</p>}
      <fieldset disabled={saving}>
        <legend className="product-form-sr-only">Ürün bilgileri</legend>
        <div className="product-form-grid">
          {fields.map((field) => (
            <div className={`product-form-field ${field.key === 'description' ? 'product-form-wide' : ''}`} key={field.key}>
              <label htmlFor={`product-edit-${field.key}`}>{field.label}{field.required && <span aria-hidden="true"> *</span>}</label>
              {field.key === 'description' ? (
                <textarea id={`product-edit-${field.key}`} name={field.key} rows={3} maxLength={field.maxLength} value={values[field.key]} onChange={(event) => setValues((previous) => ({ ...previous, [field.key]: event.target.value }))} aria-invalid={!!errors[field.key]} aria-describedby={errors[field.key] ? `product-edit-${field.key}-error` : undefined} />
              ) : (
                <input id={`product-edit-${field.key}`} name={field.key} type="text" inputMode={field.key === 'unitPrice' ? 'decimal' : field.key === 'minimumStock' ? 'numeric' : 'text'} required={field.required} maxLength={field.maxLength} value={values[field.key]} onChange={(event) => setValues((previous) => ({ ...previous, [field.key]: event.target.value }))} aria-invalid={!!errors[field.key]} aria-describedby={[errors[field.key] ? `product-edit-${field.key}-error` : '', field.key === 'unitPrice' ? 'product-edit-price-hint' : ''].filter(Boolean).join(' ') || undefined} />
              )}
              {field.key === 'barcode' && <p className="product-form-hint">Eski kodu koruyabilir veya gerçek ürün barkodunu baştaki sıfırlarla birlikte atayabilirsiniz. Ürün ve stok ilişkileri korunur.</p>}
              {field.key === 'unitPrice' && <p id="product-edit-price-hint" className="product-form-hint">Örnek: 250,50 veya 250.50. Binlik ayırıcı kullanmayın.</p>}
              {errors[field.key] && <p id={`product-edit-${field.key}-error`} className="product-field-error" role="alert">{errors[field.key]}</p>}
            </div>
          ))}
        </div>
        <div className="product-form-actions"><button type="button" className="product-cancel" onClick={onCancel}>İptal</button><button type="submit" className="products-retry">{saving ? 'Güncelleniyor...' : 'Güncelle'}</button></div>
      </fieldset>
    </form>
  )
}
