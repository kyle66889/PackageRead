# AI 质检助手 / PackageRead

A Vue 3 + ASP.NET Core app that uses **Gemini 2.5 Flash** streaming to analyze shipping labels and product photos in real time — designed for production-line workers.



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
- Tokens stream to the UI as they arrive via SSE
- Live green monospace stream display while analyzing
- Snaps into a structured result card on completion

## Architecture

```
Browser (Vue 3 · localhost:3000)
    ↕ POST /api/analyze (JSON + base64 photos)
ASP.NET Core Minimal API (localhost:5000)
    ↕ streamGenerateContent?alt=sse
Google Gemini 2.5 Flash
```

The API key lives only on the server — never shipped to the browser.

## Tech Stack

| | |
|---|---|
| Frontend | Vue 3 + Composition API, Vite 5 |
| Backend | ASP.NET Core 8 Minimal API (C#) |
| AI | Google Gemini 2.5 Flash (`gemini-2.5-flash`) |
| Streaming | Gemini SSE transparently proxied to browser |
| Camera | `navigator.mediaDevices.getUserMedia` |

## Getting Started

### 1. Clone

```bash
git clone https://github.com/kyle66889/PackageRead.git
cd PackageRead
```

### 2. Install frontend dependencies

```bash
npm install
```

### 3. Add API Key

Create `PackageReadApi/appsettings.Development.json` (gitignored — never committed):

```json
{
  "Gemini": {
    "ApiKey": "your_gemini_api_key_here"
  }
}
```

Get a free key at [Google AI Studio](https://aistudio.google.com/apikey).

### 4. Start the backend

```bash
cd PackageReadApi
dotnet run
```

Confirm you see: `Now listening on: http://localhost:5000`

### 5. Start the frontend

In a separate terminal:

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
PackageReadApi/                         # C# backend
  Program.cs                            # DI + CORS + routing
  Endpoints/AnalyzeEndpoints.cs         # POST /api/analyze
  Application/Services/GeminiService.cs # Gemini SSE proxy
  Models/                               # AnalyzeRequest, GeminiConfig
  appsettings.json                      # config skeleton (no key)
  appsettings.Development.json          # API key (gitignored)

PackageReadApi.Tests/                   # xUnit tests (10 tests)

src/
  App.vue                               # entire frontend (single-file component)
  main.js                               # Vue mount
```

## Free Tier Limits

Gemini free tier: **1,500 requests/day**, 15 RPM. Sufficient for a POC or low-volume production line.
If you hit 429 errors, wait ~1 minute (RPM reset) or check your daily quota at [ai.dev/rate-limit](https://ai.dev/rate-limit).

## License

MIT
