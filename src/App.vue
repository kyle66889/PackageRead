<script setup>
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'

// ── Mode configs ───────────────────────────────────────────────────────────
const MODES = [
  {
    id: 'shipping',
    label: '运单标签',
    icon: '📦',
    multiPhoto: false,
    autoDetect: true,
    prompt: `Extract ALL information visible on this shipping/waybill label. Return ONLY valid JSON, no markdown:
{
  "tracking_number": "tracking or waybill number",
  "carrier": "carrier name (FedEx/UPS/DHL/SF/JD/EMS/USPS etc)",
  "service": "service type or class",
  "sender_name": "sender name or company",
  "sender_address": "full sender address",
  "sender_phone": "sender phone or null",
  "recipient_name": "recipient name or company",
  "recipient_address": "full recipient address",
  "recipient_phone": "recipient phone or null",
  "weight": "weight with unit or null",
  "ship_date": "shipping date or null",
  "cod_amount": "cash on delivery amount or null",
  "notes": "any other important info or null"
}`,
    fields: [
      { key: 'tracking_number',   label: '追踪号',    span: 2, mono: true, accent: true },
      { key: 'carrier',           label: '承运商' },
      { key: 'service',           label: '服务类型' },
      { key: 'weight',            label: '重量' },
      { key: 'ship_date',         label: '发货日期' },
      { key: 'cod_amount',        label: '代收金额' },
      { key: 'sender_name',       label: '发件人' },
      { key: 'sender_phone',      label: '发件电话' },
      { key: 'sender_address',    label: '发件地址',  span: 2 },
      { key: 'recipient_name',    label: '收件人' },
      { key: 'recipient_phone',   label: '收件电话' },
      { key: 'recipient_address', label: '收件地址',  span: 2 },
      { key: 'notes',             label: '备注',      span: 2 },
    ]
  },
  {
    id: 'product',
    label: '产品质检',
    icon: '🔍',
    multiPhoto: true,
    autoDetect: false,
    hasCondition: true,
    prompt: `You are a professional product inspector. Examine ALL provided images of the same product from every angle.
Identify every label, barcode, and marking. Return ONLY valid JSON, no markdown:
{
  "product_name": "full product name and description",
  "brand": "brand or manufacturer",
  "model_number": "model number or part number or null",
  "upc_code": "12 or 13 digit UPC/EAN barcode — read it carefully from barcode image, or null",
  "serial_number": "serial number labeled S/N, SN, Serial No., or null",
  "sku": "SKU or item number if visible, else null",
  "category": "product category",
  "condition": "good/defective/warning/unknown",
  "defects": ["describe each visible defect in full detail"],
  "color": "color(s)",
  "dimensions": "size or weight from label if visible, else null",
  "packaging_condition": "sealed / open / damaged / missing",
  "quantity": "unit count or package quantity",
  "label_text": "all text visible on any label or packaging",
  "notes": "comprehensive QC assessment referencing observations from all provided images"
}`,
    fields: [
      { key: 'product_name',        label: '产品名称',       span: 2 },
      { key: 'brand',               label: '品牌' },
      { key: 'model_number',        label: '型号 / 货号' },
      { key: 'upc_code',            label: 'UPC / EAN',      span: 2, mono: true, accent: true },
      { key: 'serial_number',       label: '序列号 (S/N)',   span: 2, mono: true },
      { key: 'sku',                 label: 'SKU' },
      { key: 'category',            label: '品类' },
      { key: 'color',               label: '颜色' },
      { key: 'dimensions',          label: '尺寸 / 重量' },
      { key: 'packaging_condition', label: '包装状态' },
      { key: 'quantity',            label: '数量' },
      { key: 'label_text',          label: '标签文字',       span: 2 },
      { key: 'notes',               label: '质检详述',       span: 2 },
    ]
  },
  {
    id: 'general',
    label: '通用识别',
    icon: '🎯',
    multiPhoto: false,
    autoDetect: false,
    prompt: `Analyze this image and extract all key information. Return ONLY valid JSON, no markdown:
{
  "title": "brief descriptive title",
  "type": "object type or category",
  "text_content": "all visible text, numbers, codes or null",
  "key_info": ["important detail 1", "important detail 2"],
  "notes": "overall description"
}`,
    fields: [
      { key: 'title',        label: '识别结果', span: 2, accent: true },
      { key: 'type',         label: '类型' },
      { key: 'text_content', label: '文字内容', span: 2, mono: true },
      { key: 'key_info',     label: '关键信息', span: 2, isArray: true },
      { key: 'notes',        label: '描述',     span: 2 },
    ]
  }
]

// ── state ──────────────────────────────────────────────────────────────────
const modeId         = ref('shipping')
const appState       = ref('live')      // live | scanning | done
const videoRef       = ref(null)
const capturedUrl    = ref('')          // single-photo
const capturedPhotos = ref([])          // multi-photo [{url, base64}]
const streamText     = ref('')
const parsedResult   = ref(null)
const errorMsg       = ref('')
const autoQuality    = ref(0)
let mediaStream = null
let detectTimer = null

// ── computed ───────────────────────────────────────────────────────────────
const currentMode = computed(() => MODES.find(m => m.id === modeId.value))
const isMulti     = computed(() => currentMode.value.multiPhoto)
const resultThumb = computed(() => capturedPhotos.value[0]?.url || capturedUrl.value)

const conditionClass = computed(() => {
  const map = { good: 'badge-good', defective: 'badge-bad', warning: 'badge-warn' }
  return map[parsedResult.value?.condition?.toLowerCase()] ?? 'badge-unknown'
})
const conditionLabel = computed(() => {
  const map = { good: '✓ 合格', defective: '✗ 不合格', warning: '⚠ 待检查' }
  return map[parsedResult.value?.condition?.toLowerCase()] ?? '— 未知'
})

// clear photos when switching modes
watch(modeId, () => {
  if (appState.value !== 'live') return
  capturedPhotos.value = []
  stopAutoDetect()
  if (currentMode.value.autoDetect) startAutoDetect()
})

// ── camera ─────────────────────────────────────────────────────────────────
function stopCamera() {
  if (mediaStream) { mediaStream.getTracks().forEach(t => t.stop()); mediaStream = null }
}

async function startCamera() {
  errorMsg.value = ''
  try {
    mediaStream = await navigator.mediaDevices.getUserMedia({
      video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 } }
    })
  } catch {
    try { mediaStream = await navigator.mediaDevices.getUserMedia({ video: true }) }
    catch (e) { errorMsg.value = `摄像头访问失败: ${e.message}`; return }
  }
  appState.value = 'live'
  await nextTick()
  if (videoRef.value) videoRef.value.srcObject = mediaStream
  if (currentMode.value.autoDetect) startAutoDetect()
}

function captureBase64(video, maxW) {
  // 标签/通用类用更小分辨率(够看清文字即可),产品质检保留细节
  if (maxW == null) maxW = currentMode.value.id === 'product' ? 1024 : 768
  let w = video.videoWidth, h = video.videoHeight
  if (w > maxW) { h = Math.round(h * maxW / w); w = maxW }
  const c = document.createElement('canvas')
  c.width = w; c.height = h
  const ctx = c.getContext('2d')
  ctx.drawImage(video, 0, 0, w, h)

  // 运单/通用模式:灰度 + 提高对比度,让条码与文字更锐利(不需要颜色)
  if (currentMode.value.id !== 'product') {
    const img = ctx.getImageData(0, 0, w, h)
    const d = img.data
    const contrast = 1.4                       // 对比度系数
    const intercept = 128 * (1 - contrast)
    for (let i = 0; i < d.length; i += 4) {
      const gray = d[i] * 0.299 + d[i + 1] * 0.587 + d[i + 2] * 0.114
      let v = contrast * gray + intercept
      v = v < 0 ? 0 : v > 255 ? 255 : v
      d[i] = d[i + 1] = d[i + 2] = v
    }
    ctx.putImageData(img, 0, 0)
  }

  const url = c.toDataURL('image/jpeg', 0.82)
  return { url, base64: url.split(',')[1] }
}

// ── auto-detect (shipping only) ────────────────────────────────────────────
function getLaplacianVar(data, w, h) {
  const g = new Float32Array(w * h)
  for (let i = 0; i < w * h; i++) {
    const j = i * 4
    g[i] = data[j] * 0.299 + data[j + 1] * 0.587 + data[j + 2] * 0.114
  }
  let sum = 0, cnt = 0
  for (let y = 1; y < h - 1; y++) {
    for (let x = 1; x < w - 1; x++) {
      const i = y * w + x
      const lap = -4 * g[i] + g[i-1] + g[i+1] + g[i-w] + g[i+w]
      sum += lap * lap; cnt++
    }
  }
  return cnt ? sum / cnt : 0
}
function getBrightRatio(data) {
  let b = 0
  for (let i = 0; i < data.length; i += 4)
    if (data[i] > 155 && data[i+1] > 155 && data[i+2] > 155) b++
  return b / (data.length / 4)
}
function checkFrame() {
  if (appState.value !== 'live' || !currentMode.value.autoDetect) return
  const v = videoRef.value
  if (!v || v.readyState < 2) return
  const W = 320, H = 200
  const c = document.createElement('canvas'); c.width = W; c.height = H
  const ctx = c.getContext('2d'); ctx.drawImage(v, 0, 0, W, H)
  const { data } = ctx.getImageData(0, 0, W, H)
  const ok = getLaplacianVar(data, W, H) > 45 && getBrightRatio(data) > 0.08 && getBrightRatio(data) < 0.82
  if (ok) {
    autoQuality.value = Math.min(autoQuality.value + 1, 3)
    if (autoQuality.value >= 3) { stopAutoDetect(); singleCapture() }
  } else {
    autoQuality.value = Math.max(autoQuality.value - 1, 0)
  }
}
function startAutoDetect() {
  autoQuality.value = 0
  if (detectTimer) clearInterval(detectTimer)
  detectTimer = setInterval(checkFrame, 700)
}
function stopAutoDetect() {
  if (detectTimer) { clearInterval(detectTimer); detectTimer = null }
  autoQuality.value = 0
}

// ── capture ────────────────────────────────────────────────────────────────
function singleCapture() {
  stopAutoDetect()
  const v = videoRef.value
  if (!v || v.readyState < 2) return
  const { url, base64 } = captureBase64(v)
  capturedUrl.value = url
  stopCamera()
  beginAnalysis([{ base64 }])
}

function addPhoto() {
  if (capturedPhotos.value.length >= 6) return
  const v = videoRef.value
  if (!v || v.readyState < 2) return
  capturedPhotos.value.push(captureBase64(v))
}

function removePhoto(idx) { capturedPhotos.value.splice(idx, 1) }

function startMultiAnalysis() {
  if (!capturedPhotos.value.length) return
  stopCamera()
  beginAnalysis(capturedPhotos.value)
}

function beginAnalysis(photos) {
  streamText.value = ''
  parsedResult.value = null
  errorMsg.value = ''
  appState.value = 'scanning'
  analyzeImages(photos)
}

// ── Backend proxy streaming ────────────────────────────────────────────────
async function analyzeImages(photos) {
  const url = 'http://localhost:5000/api/analyze'
  try {
    const resp = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        mode: modeId.value,
        photos: photos.map(p => p.base64)
      })
    })
    if (!resp.ok) throw new Error(`API ${resp.status}: ${(await resp.text()).slice(0, 300)}`)

    const reader = resp.body.getReader()
    const decoder = new TextDecoder()
    let buf = '', full = ''
    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      buf += decoder.decode(value, { stream: true })
      const lines = buf.split('\n'); buf = lines.pop() ?? ''
      for (const line of lines) {
        if (!line.startsWith('data: ')) continue
        const raw = line.slice(6).trim()
        if (!raw || raw === '[DONE]') continue
        try {
          const parsed = JSON.parse(raw)
          if (parsed.error) { errorMsg.value = parsed.error; return }
          const t = parsed.candidates?.[0]?.content?.parts?.[0]?.text ?? ''
          if (t) { full += t; streamText.value = full }
        } catch { /* skip */ }
      }
    }
    const clean = full.trim().replace(/^```(?:json)?\n?/, '').replace(/\n?```$/, '')
    try { parsedResult.value = JSON.parse(clean) }
    catch { const m = full.match(/\{[\s\S]*\}/); if (m) try { parsedResult.value = JSON.parse(m[0]) } catch { /* raw */ } }
  } catch (e) { errorMsg.value = e.message }
  appState.value = 'done'
}

// ── reset ──────────────────────────────────────────────────────────────────
function nextScan() {
  capturedUrl.value = ''
  capturedPhotos.value = []
  streamText.value = ''
  parsedResult.value = null
  errorMsg.value = ''
  startCamera()
}

function fieldVal(k) { return parsedResult.value?.[k] }

onMounted(startCamera)
onUnmounted(() => { stopAutoDetect(); stopCamera() })
</script>

<template>
  <div class="app">

    <!-- ── Top bar ── -->
    <header class="topbar">
      <div class="brand">
        <svg width="22" height="22" viewBox="0 0 64 64" fill="none">
          <polygon points="32,3 61,19.5 61,44.5 32,61 3,44.5 3,19.5" stroke="#00d4ff" stroke-width="2" fill="none"/>
          <circle cx="32" cy="32" r="7" fill="#00d4ff" opacity="0.9"/>
        </svg>
        <span class="brand-name">AI 质检助手</span>
      </div>
      <div class="mode-tabs">
        <button v-for="m in MODES" :key="m.id" class="mode-tab"
          :class="{ active: modeId === m.id }" @click="modeId = m.id">
          {{ m.icon }} {{ m.label }}
        </button>
      </div>
    </header>

    <!-- ── Main ── -->
    <main class="main-grid">

      <!-- LEFT: camera -->
      <section class="panel-left">
        <div class="camera-area">

          <!-- error -->
          <div v-if="errorMsg && appState === 'live'" class="cam-placeholder">
            <p style="color:#ff7070">{{ errorMsg }}</p>
            <button class="btn-ghost" style="margin-top:8px" @click="startCamera">重试</button>
          </div>

          <!-- live video -->
          <video v-show="appState === 'live'" ref="videoRef"
            autoplay playsinline muted class="camera-video"/>

          <!-- scan frame (shipping auto-detect) -->
          <div v-if="appState === 'live' && currentMode.autoDetect" class="scan-overlay">
            <div class="scan-frame" :class="`q${autoQuality}`">
              <span class="corner c-tl"/><span class="corner c-tr"/>
              <span class="corner c-bl"/><span class="corner c-br"/>
              <div class="scan-sweep" :class="`q${autoQuality}`"/>
            </div>
            <div class="auto-badge" :class="`q${autoQuality}`">
              <span v-if="autoQuality === 0">● 自动检测中</span>
              <span v-else-if="autoQuality === 1">◐ 检测到标签</span>
              <span v-else-if="autoQuality === 2">◕ 准备捕获…</span>
              <span v-else>✓ 正在拍摄</span>
            </div>
          </div>

          <!-- captured preview (scanning/done) -->
          <img v-if="appState !== 'live'" :src="resultThumb"
            class="captured-img" alt="captured"/>
          <div v-if="appState === 'scanning'" class="scanning-mask">
            <div class="spin-ring"/>
            <span class="scanning-txt">分析中…</span>
          </div>
        </div>

        <!-- photo strip (product mode, live) -->
        <div v-if="isMulti && appState === 'live' && capturedPhotos.length" class="photo-strip">
          <div v-for="(p, i) in capturedPhotos" :key="i" class="photo-thumb-wrap">
            <img :src="p.url" class="photo-thumb"/>
            <button class="thumb-del" @click="removePhoto(i)">×</button>
          </div>
          <span class="photo-count">{{ capturedPhotos.length }} / 6</span>
        </div>

        <!-- capture bar -->
        <div class="capture-bar" :class="{ 'two-btn': isMulti && appState === 'live' }">

          <!-- product: live → two buttons -->
          <template v-if="isMulti && appState === 'live'">
            <button class="btn-action btn-add"
              :disabled="capturedPhotos.length >= 6" @click="addPhoto">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/>
                <circle cx="12" cy="13" r="4"/>
              </svg>
              拍照{{ capturedPhotos.length ? ` (${capturedPhotos.length})` : '' }}
            </button>
            <button class="btn-action btn-analyze"
              :class="capturedPhotos.length ? '' : 'btn-disabled'"
              :disabled="!capturedPhotos.length" @click="startMultiAnalysis">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              开始分析
            </button>
          </template>

          <!-- shipping/general: live → single capture -->
          <button v-else-if="appState === 'live'" class="btn-action btn-capture" @click="singleCapture">
            <span class="shutter-ring"/>
            拍照分析
          </button>

          <button v-else-if="appState === 'scanning'" class="btn-action btn-disabled" disabled>
            分析中…
          </button>

          <button v-else-if="appState === 'done'" class="btn-action btn-next" @click="nextScan">
            ↩ 下一件
          </button>
        </div>
      </section>

      <!-- RIGHT: results -->
      <section class="panel-right">

        <!-- live placeholder -->
        <div v-if="appState === 'live'" class="right-placeholder">
          <div class="placeholder-icon">{{ currentMode.icon }}</div>
          <p class="placeholder-title">{{ currentMode.label }}</p>
          <p class="placeholder-sub" v-if="isMulti">拍摄多张照片后点击「开始分析」</p>
          <p class="placeholder-sub" v-else-if="currentMode.autoDetect">自动检测到标签后拍摄</p>
          <p class="placeholder-sub" v-else>点击「拍照分析」开始识别</p>
        </div>

        <!-- streaming -->
        <div v-if="appState === 'scanning'" class="stream-panel">
          <div class="stream-head">
            <span class="live-dot"/>
            <span class="stream-label">
              实时数据流 · {{ currentMode.label }}
              <span v-if="isMulti"> · {{ capturedPhotos.length }} 张图片</span>
            </span>
          </div>
          <div v-if="errorMsg" class="alert" style="margin: 0 14px 10px">{{ errorMsg }}</div>
          <pre class="stream-body">{{ streamText || (errorMsg ? '' : '正在连接…') }}<span v-if="streamText" class="cursor">▊</span></pre>
        </div>

        <!-- result -->
        <div v-if="appState === 'done'" class="result-panel">

          <!-- multi-photo strip in result -->
          <div v-if="isMulti && capturedPhotos.length > 1" class="result-photos">
            <img v-for="(p, i) in capturedPhotos" :key="i" :src="p.url" class="result-photo"/>
          </div>

          <div class="result-head">
            <img v-if="!isMulti || capturedPhotos.length === 1" :src="resultThumb" class="result-thumb"/>
            <div class="result-head-meta">
              <span class="result-mode-label">{{ currentMode.icon }} {{ currentMode.label }}</span>
              <span v-if="parsedResult?.product_name || parsedResult?.tracking_number" class="result-title-text">
                {{ parsedResult?.product_name || parsedResult?.tracking_number }}
              </span>
              <span v-if="currentMode.hasCondition && parsedResult" class="condition-badge" :class="conditionClass">
                {{ conditionLabel }}
              </span>
            </div>
          </div>

          <div v-if="errorMsg" class="alert" style="margin: 0 14px 10px">{{ errorMsg }}</div>

          <div v-if="parsedResult" class="fields-grid">
            <template v-for="f in currentMode.fields" :key="f.key">
              <div v-if="fieldVal(f.key) !== null && fieldVal(f.key) !== undefined && fieldVal(f.key) !== ''"
                class="field-cell" :class="{ 'span-2': f.span === 2, accent: f.accent }">
                <span class="field-label">{{ f.label }}</span>
                <div v-if="f.isArray && Array.isArray(fieldVal(f.key))" class="tag-list">
                  <span v-for="it in fieldVal(f.key)" :key="it" class="tag">{{ it }}</span>
                </div>
                <span v-else class="field-val" :class="{ mono: f.mono }">{{ fieldVal(f.key) }}</span>
              </div>
            </template>
            <div v-if="currentMode.hasCondition && parsedResult?.defects?.length"
              class="field-cell span-2 defect-cell">
              <span class="field-label">⚠ 缺陷</span>
              <div class="tag-list">
                <span v-for="d in parsedResult.defects" :key="d" class="tag tag-bad">{{ d }}</span>
              </div>
            </div>
          </div>

          <div v-else-if="streamText" class="raw-panel">
            <span class="field-label">原始返回</span>
            <pre class="raw-text">{{ streamText }}</pre>
          </div>

          <div v-else-if="!errorMsg" class="right-placeholder" style="padding: 20px">
            <p class="placeholder-sub">未识别到结果，请重试</p>
          </div>
        </div>

      </section>
    </main>
  </div>
</template>

<style scoped>
/* ─── Base ────────────────────────────────────────────────────────────────── */
.app {
  display: flex; flex-direction: column; height: 100vh;
  background: #080810; color: #e8eaf6;
  font-family: -apple-system, 'PingFang SC', 'Microsoft YaHei', 'Segoe UI', sans-serif;
  overflow: hidden;
}

/* ─── Topbar ──────────────────────────────────────────────────────────────── */
.topbar {
  flex-shrink: 0; height: 52px;
  display: flex; align-items: center; padding: 0 16px; gap: 16px;
  background: #0d0d1a; border-bottom: 1px solid rgba(255,255,255,0.07);
}
.brand { display: flex; align-items: center; gap: 8px; flex-shrink: 0; }
.brand svg { filter: drop-shadow(0 0 6px rgba(0,212,255,0.5)); }
.brand-name { font-size: 15px; font-weight: 700; color: #fff; white-space: nowrap; }
.mode-tabs { display: flex; gap: 4px; flex: 1; justify-content: center; }
.mode-tab {
  padding: 6px 14px; border-radius: 6px; border: 1px solid rgba(255,255,255,0.1);
  background: transparent; color: #7a80a0; font-size: 13px; cursor: pointer;
  transition: all 0.15s; white-space: nowrap;
}
.mode-tab:hover { border-color: rgba(0,212,255,0.3); color: #c0c8e8; }
.mode-tab.active { background: rgba(0,212,255,0.1); border-color: rgba(0,212,255,0.4); color: #00d4ff; font-weight: 600; }

/* ─── Main grid ───────────────────────────────────────────────────────────── */
.main-grid { flex: 1; display: grid; grid-template-columns: minmax(280px, 42%) 1fr; min-height: 0; overflow: hidden; }

/* ─── Left panel ──────────────────────────────────────────────────────────── */
.panel-left { display: flex; flex-direction: column; border-right: 1px solid rgba(255,255,255,0.07); background: #000; overflow: hidden; }

.camera-area { flex: 1; position: relative; overflow: hidden; min-height: 0; }
.cam-placeholder { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; color: #3d4260; font-size: 13px; }
.camera-video { width: 100%; height: 100%; object-fit: cover; display: block; }
.captured-img { width: 100%; height: 100%; object-fit: contain; background: #050508; display: block; }

/* Scan frame */
.scan-overlay { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; pointer-events: none; }
.scan-frame { position: relative; width: min(80%, 340px); height: min(60%, 220px); }
.corner { position: absolute; width: 18px; height: 18px; border-color: rgba(0,212,255,0.5); border-style: solid; transition: border-color 0.3s; }
.c-tl { top:0; left:0; border-width:2px 0 0 2px; }
.c-tr { top:0; right:0; border-width:2px 2px 0 0; }
.c-bl { bottom:0; left:0; border-width:0 0 2px 2px; }
.c-br { bottom:0; right:0; border-width:0 2px 2px 0; }
.scan-sweep { position: absolute; left:0; right:0; height:1px; background: linear-gradient(90deg, transparent, #00d4ff, transparent); box-shadow: 0 0 6px rgba(0,212,255,0.5); animation: sweep 2s ease-in-out infinite; }
@keyframes sweep { 0% { top:0%; opacity:0; } 5% { opacity:1; } 95% { opacity:1; } 100% { top:100%; opacity:0; } }

.q1 .corner { border-color: rgba(255,200,50,0.85); }
.q2 .corner { border-color: rgba(80,255,140,0.9); }
.q3 .corner { border-color: #00ff88; }
.q1 .scan-sweep { background: linear-gradient(90deg, transparent, #ffc832, transparent); }
.q2 .scan-sweep { background: linear-gradient(90deg, transparent, #50ff8c, transparent); }
.q3 .scan-sweep { background: linear-gradient(90deg, transparent, #00ff88, transparent); }

.auto-badge { margin-top: 10px; padding: 4px 12px; border-radius: 99px; font-size: 11px; letter-spacing: 0.8px; transition: all 0.3s; }
.auto-badge.q0 { background: rgba(0,212,255,0.07); color: rgba(0,212,255,0.45); border: 1px solid rgba(0,212,255,0.12); }
.auto-badge.q1 { background: rgba(255,200,50,0.1); color: rgba(255,200,50,0.9); border: 1px solid rgba(255,200,50,0.3); }
.auto-badge.q2 { background: rgba(80,255,140,0.1); color: rgba(80,255,140,0.95); border: 1px solid rgba(80,255,140,0.3); }
.auto-badge.q3 { background: rgba(0,255,136,0.15); color: #00ff88; border: 1px solid rgba(0,255,136,0.4); font-weight: 600; }

/* Scanning overlay */
.scanning-mask { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 12px; background: rgba(8,8,16,0.55); }
.spin-ring { width: 44px; height: 44px; border-radius: 50%; border: 3px solid rgba(0,212,255,0.15); border-top-color: #00d4ff; animation: spin 0.7s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
.scanning-txt { font-size: 12px; color: #00d4ff; letter-spacing: 3px; text-transform: uppercase; animation: fpulse 1.4s ease-in-out infinite; }
@keyframes fpulse { 0%,100% { opacity:.4; } 50% { opacity:1; } }

/* Photo strip */
.photo-strip { flex-shrink: 0; display: flex; align-items: center; gap: 6px; padding: 8px 10px; background: #0a0a14; border-top: 1px solid rgba(255,255,255,0.05); overflow-x: auto; }
.photo-thumb-wrap { position: relative; flex-shrink: 0; }
.photo-thumb { width: 52px; height: 52px; object-fit: cover; border-radius: 5px; border: 1px solid rgba(255,255,255,0.1); display: block; }
.thumb-del { position: absolute; top: -5px; right: -5px; width: 16px; height: 16px; border-radius: 50%; background: #ff5252; border: none; color: #fff; font-size: 10px; cursor: pointer; display: flex; align-items: center; justify-content: center; line-height: 1; }
.photo-count { flex-shrink: 0; font-size: 11px; color: #5a6080; margin-left: 4px; }

/* Capture bar */
.capture-bar { flex-shrink: 0; padding: 10px 12px; background: #0a0a14; border-top: 1px solid rgba(255,255,255,0.05); }
.two-btn { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }

.btn-action {
  width: 100%; padding: 12px; border-radius: 8px; border: none;
  font-size: 14px; font-weight: 600; cursor: pointer;
  display: flex; align-items: center; justify-content: center; gap: 7px;
  transition: all 0.15s; -webkit-tap-highlight-color: transparent;
}
.btn-capture  { background: #00d4ff; color: #080810; }
.btn-capture:hover { background: #00bce0; }
.btn-capture:active { transform: scale(0.97); }
.btn-add      { background: rgba(0,212,255,0.12); color: #00d4ff; border: 1px solid rgba(0,212,255,0.25); }
.btn-add:hover:not(:disabled) { background: rgba(0,212,255,0.2); }
.btn-analyze  { background: rgba(0,230,118,0.12); color: #00e676; border: 1px solid rgba(0,230,118,0.25); }
.btn-analyze:hover:not(:disabled) { background: rgba(0,230,118,0.2); }
.btn-disabled { background: rgba(255,255,255,0.04); color: #3d4260; cursor: not-allowed; border: 1px solid rgba(255,255,255,0.05); }
.btn-next     { background: rgba(0,230,118,0.1); color: #00e676; border: 1px solid rgba(0,230,118,0.2); }
.btn-next:hover { background: rgba(0,230,118,0.18); }
.shutter-ring { width: 14px; height: 14px; border-radius: 50%; border: 2px solid #080810; }

/* ─── Right panel ─────────────────────────────────────────────────────────── */
.panel-right { display: flex; flex-direction: column; overflow-y: auto; background: #080810; }

.right-placeholder { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; padding: 32px; color: #3d4260; text-align: center; }
.placeholder-icon { font-size: 36px; opacity: 0.35; }
.placeholder-title { font-size: 16px; font-weight: 600; color: #5a6080; }
.placeholder-sub { font-size: 12px; }

/* Stream */
.stream-panel { flex: 1; display: flex; flex-direction: column; overflow: hidden; min-height: 0; }
.stream-head { flex-shrink: 0; display: flex; align-items: center; gap: 8px; padding: 11px 16px; border-bottom: 1px solid rgba(255,255,255,0.05); }
.live-dot { width: 7px; height: 7px; border-radius: 50%; background: #00e676; animation: lpulse 1.1s ease-in-out infinite; }
@keyframes lpulse { 0%,100% { box-shadow: 0 0 0 0 rgba(0,230,118,0.5); } 50% { box-shadow: 0 0 0 5px rgba(0,230,118,0); } }
.stream-label { font-size: 11px; color: #5a6080; text-transform: uppercase; letter-spacing: 1.5px; }
.stream-body { flex: 1; padding: 12px 16px; font-family: 'Fira Code', 'Cascadia Code', 'Courier New', monospace; font-size: 12px; line-height: 1.7; color: #7fffb0; white-space: pre-wrap; word-break: break-all; overflow-y: auto; min-height: 0; }
.cursor { color: #00d4ff; animation: blink 0.8s step-end infinite; }
@keyframes blink { 0%,100% { opacity:1; } 50% { opacity:0; } }

/* Result */
.result-panel { flex: 1; display: flex; flex-direction: column; }

/* multi-photo row */
.result-photos { flex-shrink: 0; display: flex; gap: 6px; padding: 12px 14px 0; overflow-x: auto; }
.result-photo { width: 64px; height: 64px; object-fit: cover; border-radius: 6px; border: 1px solid rgba(255,255,255,0.1); flex-shrink: 0; }

.result-head { display: flex; align-items: flex-start; gap: 12px; padding: 12px 14px; border-bottom: 1px solid rgba(255,255,255,0.05); flex-shrink: 0; }
.result-thumb { width: 68px; height: 68px; object-fit: cover; border-radius: 8px; border: 1px solid rgba(255,255,255,0.08); flex-shrink: 0; }
.result-head-meta { flex: 1; display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.result-mode-label { font-size: 11px; color: #5a6080; text-transform: uppercase; letter-spacing: 0.8px; }
.result-title-text { font-size: 15px; font-weight: 600; color: #fff; word-break: break-word; line-height: 1.3; }

.condition-badge { display: inline-block; padding: 3px 10px; border-radius: 99px; font-size: 12px; font-weight: 600; align-self: flex-start; }
.badge-good    { background: rgba(0,230,118,0.12); color:#00e676; border:1px solid rgba(0,230,118,0.25); }
.badge-bad     { background: rgba(255,82,82,0.12);  color:#ff5252; border:1px solid rgba(255,82,82,0.25); }
.badge-warn    { background: rgba(255,171,0,0.12);  color:#ffab00; border:1px solid rgba(255,171,0,0.25); }
.badge-unknown { background: rgba(90,96,128,0.1);  color:#7a80a0; border:1px solid rgba(90,96,128,0.2); }

.fields-grid { padding: 12px 14px; display: grid; grid-template-columns: 1fr 1fr; gap: 7px; }
.field-cell { background: #0d0d1c; border: 1px solid rgba(255,255,255,0.05); border-radius: 7px; padding: 9px 11px; display: flex; flex-direction: column; gap: 4px; min-width: 0; }
.field-cell.span-2 { grid-column: span 2; }
.field-cell.accent { border-color: rgba(0,212,255,0.2); background: rgba(0,212,255,0.04); }
.field-cell.defect-cell { border-color: rgba(255,82,82,0.18); background: rgba(255,82,82,0.04); grid-column: span 2; }
.field-label { font-size: 10px; color: #5a6080; text-transform: uppercase; letter-spacing: 0.8px; }
.field-val { font-size: 13px; color: #d0d4f0; font-weight: 500; word-break: break-word; line-height: 1.4; }
.field-val.mono { font-family: 'Fira Code', monospace; font-size: 12px; color: #a0f0c0; }
.field-cell.accent .field-val { font-size: 15px; color: #e8f4ff; font-weight: 700; }

.tag-list { display: flex; flex-wrap: wrap; gap: 5px; }
.tag { background: rgba(0,212,255,0.08); border: 1px solid rgba(0,212,255,0.2); color: #80d8f0; border-radius: 4px; padding: 2px 8px; font-size: 12px; }
.tag-bad { background: rgba(255,82,82,0.08); border-color: rgba(255,82,82,0.2); color: #ff8080; }

.raw-panel { margin: 12px 14px; background: #0d0d1c; border: 1px solid rgba(255,255,255,0.05); border-radius: 7px; padding: 11px; }
.raw-text { font-family: 'Fira Code', monospace; font-size: 11px; color: #7fffb0; white-space: pre-wrap; word-break: break-all; margin: 0; }

.alert { padding: 10px 13px; background: rgba(255,82,82,0.08); border: 1px solid rgba(255,82,82,0.18); border-radius: 7px; color: #ff7070; font-size: 12px; line-height: 1.4; }
.btn-ghost { background: transparent; border: 1px solid rgba(255,255,255,0.12); color: #b0b8d8; border-radius: 7px; padding: 8px 16px; font-size: 13px; cursor: pointer; }
</style>
