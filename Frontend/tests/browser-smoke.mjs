// Dependency-free Chrome/CDP checks. Every API request is intercepted in this
// isolated test browser; fixture mutations never reach the real backend.
import { spawn } from 'node:child_process'
import { mkdtemp, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import assert from 'node:assert/strict'

const origin = 'http://localhost:5173'
const profile = await mkdtemp(path.join(os.tmpdir(), 'inventory-auth-browser-'))
const chrome = spawn(process.env.CHROME_PATH ?? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', [
  '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1', `--user-data-dir=${profile}`, 'about:blank',
], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] })
let socket, session, counter = 0
const pending = new Map()
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))
const timer = setTimeout(() => { chrome.kill(); process.exitCode = 1 }, 90000)
const failures = []

try {
  const endpoint = await new Promise((resolve, reject) => {
    let output = ''
    chrome.stderr.on('data', (chunk) => { output += chunk; const match = output.match(/DevTools listening on (ws:\/\/[^\s]+)/); if (match) resolve(match[1]) })
    chrome.on('error', reject)
    chrome.on('exit', () => reject(new Error('Isolated browser exited')))
  })
  socket = new WebSocket(endpoint)
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }) })
  function command(method, params = {}, useSession = true) {
    const id = ++counter
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject })
      socket.send(JSON.stringify({ id, method, params, ...(useSession && session ? { sessionId: session } : {}) }))
    })
  }
  const id = '00000000-0000-0000-0000-000000000001'
  let role = null, nextRole = 'Admin', bootstrapFails = false, logoutFails = false, loginFails = false
  let productStatus = 200, createStatus = 409, loginCount = 0, logoutCount = 0, writes = 0, invalidCsrf = 0
  let products = []
  const fixtureToken = 'browser-fixture-token'
  async function intercept(event) {
    const { requestId, request } = event
    const url = new URL(request.url)
    let status = 200, data = []
    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method)) {
      writes++
      const token = Object.entries(request.headers).find(([name]) => name.toLowerCase() === 'x-csrf-token')?.[1]
      if (token !== fixtureToken) invalidCsrf++
    }
    if (url.pathname === '/api/auth/csrf') data = { requestToken: fixtureToken, headerName: 'X-CSRF-TOKEN' }
    else if (url.pathname === '/api/auth/me') {
      status = bootstrapFails ? 500 : role ? 200 : 401
      data = role ? { id, email: 'browser-fixture@example.test', roles: [role] } : {}
    } else if (url.pathname === '/api/auth/login') {
      loginCount++
      status = loginFails ? 429 : 204
      if (!loginFails) role = nextRole
    } else if (url.pathname === '/api/auth/logout') {
      logoutCount++
      status = logoutFails ? 500 : 204
      if (!logoutFails) role = null
    } else if (url.pathname === '/api/customer-portal/me') {
      data = { name: 'Owned browser customer' }
    } else if (url.pathname === '/api/products' && request.method === 'POST') {
      status = createStatus
      if (status === 201) {
        const body = JSON.parse(request.postData)
        products = [{ ...body, id, isActive: true, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null }]
        data = products[0]
      }
    } else if (url.pathname === '/api/products') { status = productStatus; data = products }
    await command('Fetch.fulfillRequest', { requestId, responseCode: status,
      responseHeaders: [{ name: 'Content-Type', value: 'application/json' }],
      ...(status !== 204 ? { body: Buffer.from(JSON.stringify(data)).toString('base64') } : {}),
    })
  }
  socket.addEventListener('message', (event) => {
    const message = JSON.parse(event.data)
    if (message.id) {
      const entry = pending.get(message.id)
      pending.delete(message.id)
      if (message.error) entry?.reject(new Error(message.error.message))
      else entry?.resolve(message.result)
    } else if (message.method === 'Fetch.requestPaused') void intercept(message.params).catch((error) => {
      // StrictMode and route changes can cancel a request before CDP fulfills it.
      if (!/Invalid InterceptionId|Invalid interceptionId|No resource with given identifier/i.test(error.message)) failures.push(`API interception failed: ${error.message}`)
    })
    else if (message.method === 'Runtime.exceptionThrown') failures.push('Uncaught browser exception')
  })
  const target = await command('Target.createTarget', { url: 'about:blank' }, false)
  session = (await command('Target.attachToTarget', { targetId: target.targetId, flatten: true }, false)).sessionId
  await command('Runtime.enable')
  await command('Page.enable')
  await command('Fetch.enable', { patterns: [{ urlPattern: `${origin}/api/*`, requestStage: 'Request' }] })
  const evaluate = async (expression) => {
    const result = await command('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })
    if (result.exceptionDetails) throw new Error('Browser assertion evaluation failed')
    return result.result.value
  }
  async function waitFor(expression) {
    for (let attempt = 0; attempt < 100; attempt++) {
      if (await evaluate(`Boolean(${expression})`)) return
      await sleep(60)
    }
    throw new Error(`Browser condition timed out: ${expression.slice(0, 140)}`)
  }
  const navigate = async (route) => { await command('Page.navigate', { url: origin + route }) }
  const textIncludes = (text) => `document.body.innerText.includes(${JSON.stringify(text)})`
  async function click(text) {
    await waitFor(`[...document.querySelectorAll('button')].some(x=>x.textContent.trim()===${JSON.stringify(text)}&&!x.disabled)`)
    await evaluate(`[...document.querySelectorAll('button')].find(x=>x.textContent.trim()===${JSON.stringify(text)}&&!x.disabled).click()`)
  }
  async function fill(selector, value) {
    await evaluate(`(()=>{const x=document.querySelector(${JSON.stringify(selector)});Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(x,${JSON.stringify(value)});x.dispatchEvent(new Event('input',{bubbles:true}));})()`)
  }
  async function loginAs(next) {
    nextRole = next
    await waitFor("!!document.querySelector('#login-email')")
    await fill('#login-email', 'browser-fixture@example.test')
    await fill('#login-password', 'test-fixture-not-a-real-password')
    const before = loginCount
    await evaluate("(()=>{const form=document.querySelector('form');form.requestSubmit();form.requestSubmit();})()")
    const home = next === 'Customer' ? '/portal' : next === 'WarehouseStaff' ? '/stock' : '/dashboard'
    await waitFor(`location.pathname===${JSON.stringify(home)} && document.querySelector('.sidebar-session')`)
    assert.equal(loginCount, before + 1)
  }
  const passed = (name) => console.log(`PASS ${name}`)

  bootstrapFails = true
  await navigate('/products')
  await waitFor(textIncludes('Oturum kontrol edilemedi'))
  assert.equal(await evaluate("!!document.querySelector('#login-email')"), false)
  bootstrapFails = false
  await click('Tekrar Dene')
  await waitFor("location.pathname==='/login' && !!document.querySelector('#login-email')")
  assert.equal(await evaluate("!!document.querySelector('.app-shell')"), false)
  passed('bootstrap server failure stays recoverable; anonymous ERP route redirects to login')

  await fill('#login-email', 'browser-fixture@example.test')
  await fill('#login-password', 'test-fixture-not-a-real-password')
  await evaluate("document.querySelector('[aria-label=\"Parolayı göster\"]').click()")
  assert.equal(await evaluate("document.querySelector('#login-password').type"), 'text')
  await evaluate("document.querySelector('[aria-label=\"Parolayı gizle\"]').click()")
  loginFails = true
  await click('Giriş Yap')
  await waitFor(textIncludes('Çok fazla istek'))
  assert.equal(loginCount, 1)
  loginFails = false
  await loginAs('Admin')
  await command('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true })
  await evaluate("document.querySelector('.mobile-toggle').click()")
  await waitFor("document.querySelector('#mobile-navigation').open")
  assert.equal(await evaluate("document.body.classList.contains('menu-open')"), true)
  await evaluate("document.querySelector('.sidebar-close').click()")
  await waitFor("!document.querySelector('#mobile-navigation').open")
  await command('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false })
  passed('password visibility, login 429, successful login and verified /me navigation')

  await navigate('/login')
  await waitFor("location.pathname==='/dashboard' && !!document.querySelector('.app-shell')")
  await navigate('/products')
  await waitFor(textIncludes('Henüz ürün eklenmemiş.'))
  await click('Yeni Ürün Ekle')
  const before = writes
  await click('Kaydet')
  await sleep(100)
  assert.equal(writes, before)
  await fill('[name="barcode"]', '00036000291452')
  await fill('[name="name"]', 'Browser fixture')
  await fill('[name="unitPrice"]', '12,50')
  await fill('[name="minimumStock"]', '1')
  await click('Kaydet')
  await waitFor(textIncludes('zaten kullanılıyor'))
  assert.equal(await evaluate('document.querySelector(\'[name="barcode"]\').value'), '00036000291452')
  createStatus = 201
  await click('Kaydet')
  await waitFor(textIncludes('Ürün başarıyla eklendi.'))
  await waitFor("!!document.querySelector('tbody tr')")
  passed('Admin create visibility, required validation, 409 retains values, 201 refreshes table (intercepted only)')

  productStatus = 403
  await navigate('/products')
  await waitFor(textIncludes('Bu işlem için yetkiniz bulunmuyor.'))
  assert.equal(await evaluate("location.pathname"), '/products')
  assert.equal(await evaluate("!!document.querySelector('.sidebar-session')"), true)
  productStatus = 200
  logoutFails = true
  await click('Çıkış Yap')
  await waitFor(textIncludes('Sunucu oturumu açık olabilir'))
  assert.equal(logoutCount, 1)
  assert.equal(await evaluate("!!document.querySelector('.app-shell')"), true)
  logoutFails = false
  await click('Çıkış Yap')
  await waitFor("location.pathname==='/login' && !!document.querySelector('#login-email')")
  assert.equal(await evaluate("!!document.querySelector('.app-shell')"), false)
  passed('403 preserves session; failed logout warns; successful logout removes private UI')

  await loginAs('Viewer')
  await navigate('/products')
  await waitFor("!!document.querySelector('tbody tr')")
  for (const label of ['Yeni Ürün Ekle', 'Pasif Ürünler', 'Düzenle', 'Pasifleştir']) assert.equal(await evaluate(textIncludes(label)), false)
  await navigate('/warehouses')
  await waitFor(textIncludes('Henüz depo eklenmemiş.'))
  assert.equal(await evaluate(textIncludes('Yeni Depo Ekle')), false)
  assert.equal(await evaluate(textIncludes('Pasif Depolar')), false)
  await navigate('/stock')
  await waitFor(textIncludes('Henüz stok kaydı bulunmuyor.'))
  assert.equal(await evaluate("document.querySelectorAll('.stock-toolbar button').length"), 0)
  assert.equal(await evaluate("document.querySelectorAll('nav a[href=\"/customers\"]').length"), 0)
  await navigate('/customers')
  await waitFor(textIncludes('Erişim engellendi'))
  passed('Viewer read-only catalog/stock UI; customer route and navigation denied')
  await click('Çıkış Yap')
  await loginAs('WarehouseStaff')
  await navigate('/stock')
  await waitFor(textIncludes('Henüz stok kaydı bulunmuyor.'))
  assert.equal(await evaluate("document.querySelectorAll('.stock-toolbar button').length"), 2)
  await click('Stok Girişi')
  await waitFor("!!document.querySelector('#stock-in-form')")
  await click('İptal')
  assert.equal(await evaluate("!!document.querySelector('#stock-in-form')"), false)
  passed('WarehouseStaff stock actions visible; cancellation does not write')

  await click('Çıkış Yap')
  await loginAs('Customer')
  await waitFor(textIncludes('Owned browser customer'))
  assert.equal(await evaluate("document.querySelectorAll('nav a[href=\"/stock\"]').length"), 0)
  assert.equal(await evaluate("document.querySelectorAll('nav a[href=\"/customers\"]').length"), 0)
  await navigate('/products')
  await waitFor(textIncludes('Erişim engellendi'))
  await navigate('/')
  await waitFor("location.pathname==='/portal'")
  await click('Çıkış Yap')
  await loginAs('WarehouseStaff')
  passed('Customer verified home, owned portal greeting, hidden management links and direct access denied')

  productStatus = 401
  await navigate('/products')
  await waitFor("location.pathname==='/login' && !!document.querySelector('#login-email')")
  await waitFor(textIncludes('Oturumunuz bulunamadı veya sona erdi'))
  await command('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true })
  assert.equal(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), true)
  assert.equal(invalidCsrf, 0)
  assert.equal(await evaluate('localStorage.length + sessionStorage.length'), 0)
  assert.deepEqual(failures, [])
  passed('expired session redirects safely; mobile login fits; every intercepted write has CSRF; storage stays empty')
  console.log('Browser checks completed. Real API mutation requests: 0.')
} finally {
  clearTimeout(timer)
  socket?.close()
  chrome.kill()
  await sleep(500)
  const resolved = path.resolve(profile)
  if (resolved.startsWith(path.resolve(os.tmpdir()) + path.sep + 'inventory-auth-browser-')) {
    await rm(resolved, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }).catch(() => {})
  }
}
