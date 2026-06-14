# AI 质检助手 / PackageRead

A Vue 3 web app that uses **Gemini 2.5 Flash** streaming to analyze shipping labels and product photos in real time — designed for production-line workers.

![screenshot](https://github.com/kyle66889/PackageRead/raw/main/docs/screenshot.png)

## Features

### 运单标签 (Shipping Label)
- **Auto-detect**: local sharpness + brightness analysis runs every 700ms
- Automatically captures when a clear label is detected (no button click needed)
- Extracts: tracking number, carrier, service type, sender/recipient address & phone, weight, ship date, COD amount

### 产品质检 (Product Inspection)
- **Multi-photo**: capture up to 6 photos of the same product from different angles
- Manual capture — click "拍照" to add each shot, "开始分析" to send all at once
- Extracts: product name, brand, model number, **UPC/EAN barcode**, **serial number (S/N)**, SKU, condition, defects, packaging state, all label text

### 通用识别 (General Recognition)
- Single photo, manual capture
- Extracts: title, type, visible text content, key info

### Real-time Streaming
- Uses `streamGenerateContent?alt=sse` — tokens stream to the UI as they arrive
- Live green monospace stream display while analyzing
- Snaps into a structured result card on completion

## Tech Stack

| | |
|---|---|
| Framework | Vue 3 + Composition API |
| Build | Vite 5 |
| AI | Google Gemini 2.5 Flash (`gemini-2.5-flash`) |
| Streaming | Gemini SSE (`streamGenerateContent?alt=sse`) |
| Camera | `navigator.mediaDevices.getUserMedia` |

## Getting Started

### 1. Clone

```bash
git clone https://github.com/kyle66889/PackageRead.git
cd PackageRead
```

### 2. Install

```bash
npm install
```

### 3. Add API Key

Create a `.env` file in the project root:

```
VITE_GEMINI_API_KEY=your_gemini_api_key_here
```

Get a free key at [Google AI Studio](https://aistudio.google.com/apikey).

> **Note:** `.env` is in `.gitignore` — your key is never committed.

### 4. Run

```bash
npm run dev
```

Open [http://localhost:3000](http://localhost:3000) in your browser. Grant camera access when prompted.

## Usage

### Shipping Label
1. Point camera at a shipping label
2. Auto-detection runs in the background — corners turn green when ready
3. Captures and analyzes automatically

### Product Inspection
1. Switch to "产品质检" tab
2. Click **📷 拍照** to capture each angle (barcode, serial number label, product face, etc.)
3. Up to 6 photos — thumbnails appear below the camera; click **×** to remove any
4. Click **🔍 开始分析** to send all photos to Gemini at once

## Project Structure

```
src/
  App.vue       # entire app (single-file component)
  main.js       # Vue mount
index.html
vite.config.js
.env            # VITE_GEMINI_API_KEY (not committed)
```

## Free Tier Limits

Gemini free tier: **1,500 requests/day**, 15 RPM. Sufficient for a POC or low-volume production line.
If you hit 429 errors, wait ~1 minute (RPM reset) or check your daily quota at [ai.dev/rate-limit](https://ai.dev/rate-limit).

## License

MIT
