import { apiFetch } from '../../api/apiFetch'
import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'

type Field = 'code' | 'name' | 'email' | 'phone' | 'taxNumber' | 'address'
type FieldErrors = Partial<Record<Field, string>>
const fields: { key: Field; label: string; maxLength: number; required: boolean; type?: 'text' | 'email' | 'tel' }[] = [
  { key: 'code', label: 'Müşteri Kodu', maxLength: 32, required: true },
  { key: 'name', label: 'Müşteri Adı', maxLength: 200, required: true },
  { key: 'email', label: 'E-posta', maxLength: 254, required: false, type: 'email' },
  { key: 'phone', label: 'Telefon', maxLength: 32, required: false, type: 'tel' },
  { key: 'taxNumber', label: 'Vergi Numarası', maxLength: 32, required: false },
  { key: 'address', label: 'Adres', maxLength: 500, required: false },
]
const uncertainMessage = 'Müşteri kaydının gerçekleşip gerçekleşmediği doğrulanamadı. Tekrar göndermeden önce müşteri listesini kontrol edin.'

export function CustomerCreateForm({ onSuccess, onCancel }: { onSuccess: () => void; onCancel: () => void }) {
  const [values, setValues] = useState<Record<Field, string>>({ code: '', name: '', email: '', phone: '', taxNumber: '', address: '' })
  const [errors, setErrors] = useState<FieldErrors>({})
  const [generalError, setGeneralError] = useState('')
  const [saving, setSaving] = useState(false)
  const formRef = useRef<HTMLFormElement>(null)
  const submittingRef = useRef(false)
  const mountedRef = useRef(false)

  useEffect(() => {
    mountedRef.current = true
    formRef.current?.querySelector<HTMLInputElement>('[name="code"]')?.focus()
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
      // A browser request cancellation does not roll back a server-side customer creation.
    }
  }, [])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submittingRef.current) return
    const trimmed: Record<Field, string> = {
      code: values.code.trim(), name: values.name.trim(), email: values.email.trim(),
      phone: values.phone.trim(), taxNumber: values.taxNumber.trim(), address: values.address.trim(),
    }
    const nextErrors: FieldErrors = {}
    for (const field of fields) {
      if (field.required && !trimmed[field.key]) nextErrors[field.key] = `${field.label} zorunludur; yalnızca boşluk içeremez.`
      else if (trimmed[field.key].length > field.maxLength) nextErrors[field.key] = `${field.label} en fazla ${field.maxLength} karakter olabilir.`
    }
    const emailControl = formRef.current?.elements.namedItem('email')
    if (trimmed.email && !nextErrors.email && emailControl instanceof HTMLInputElement && emailControl.validity.typeMismatch) {
      nextErrors.email = 'Geçerli bir e-posta adresi girin.'
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
    try {
      const response = await apiFetch('/api/customers', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code: trimmed.code, name: trimmed.name, email: trimmed.email || null, phone: trimmed.phone || null, taxNumber: trimmed.taxNumber || null, address: trimmed.address || null }),
      })
      if (!mountedRef.current) return
      if (response.status === 201) {
        onSuccess()
        return
      }
      if (response.status === 409) {
        setErrors({ code: 'Bu müşteri kodu zaten kullanılıyor. Farklı bir kod girin.' })
      } else if (response.status === 400) {
        const problem: unknown = await response.json().catch(() => null)
        if (!mountedRef.current) return
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
        if (otherErrors.length || !Object.keys(backendErrors).length) setGeneralError(otherErrors.join(' ') || 'Müşteri bilgileri kabul edilmedi. Alanları kontrol edin.')
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
    <form ref={formRef} id="customer-create-form" className="product-create-form customer-create-form" onSubmit={submit} noValidate aria-labelledby="customer-create-title" aria-busy={saving}>
      <div className="product-form-heading"><h2 id="customer-create-title">Yeni Müşteri Ekle</h2><p>Yıldız (*) ile işaretli alanlar zorunludur. Diğer bilgileri isteğe bağlı olarak ekleyebilirsiniz.</p></div>
      {generalError && <p className="product-form-error" role="alert">{generalError}</p>}
      {saving && <p className="customer-create-progress" role="status">Kaydediliyor... İşlem tamamlanana kadar bu sayfada bekleyin.</p>}
      <fieldset disabled={saving}>
        <legend className="product-form-sr-only">Müşteri bilgileri</legend>
        <div className="product-form-grid">
          {fields.map((field) => <div key={field.key} className={`product-form-field ${field.key === 'address' ? 'customer-form-wide' : ''}`}>
            <label htmlFor={`customer-${field.key}`}>{field.label}{field.required && <span aria-hidden="true"> *</span>}</label>
            {field.key === 'address' ? (
              <textarea id={`customer-${field.key}`} name={field.key} rows={3} maxLength={field.maxLength} value={values[field.key]} onChange={(event) => { const value = event.target.value; setValues((previous) => ({ ...previous, [field.key]: value })) }} aria-invalid={!!errors[field.key]} aria-describedby={errors[field.key] ? `customer-${field.key}-error` : undefined} />
            ) : (
              <input id={`customer-${field.key}`} name={field.key} type={field.type ?? 'text'} required={field.required} maxLength={field.maxLength} value={values[field.key]} onChange={(event) => { const value = event.target.value; setValues((previous) => ({ ...previous, [field.key]: value })) }} aria-invalid={!!errors[field.key]} aria-describedby={errors[field.key] ? `customer-${field.key}-error` : undefined} />
            )}
            {errors[field.key] && <p id={`customer-${field.key}-error`} className="product-field-error" role="alert">{errors[field.key]}</p>}
          </div>)}
        </div>
        <div className="product-form-actions"><button type="button" className="product-cancel" onClick={() => { if (!submittingRef.current) onCancel() }}>İptal</button><button type="submit" className="customers-retry">{saving ? 'Kaydediliyor...' : 'Kaydet'}</button></div>
      </fieldset>
    </form>
  )
}
