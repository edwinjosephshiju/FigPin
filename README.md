# FigPin Layer Separator Studio - AI 🎨✨

[![Build & Package MSIX](https://github.com/edwinjosephshiju/FigPin/actions/workflows/build-msix.yml/badge.svg)](https://github.com/edwinjosephshiju/FigPin/actions/workflows/build-msix.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Framework](https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK-0078D4.svg)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
[![CUDA Acceleration](https://img.shields.io/badge/PyTorch-CUDA%2012.1-76B900.svg)](https://pytorch.org/)
[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-9ND5QL4M5T5R-0078D4.svg)](https://partner.microsoft.com/)

**FigPin Layer Separator Studio** is a state-of-the-art Windows desktop application powered by **WinUI 3 (Windows App SDK)** and a high-performance **PyTorch CUDA AI pipeline**. It automatically deconstructs complex graphics, posters, and 3D artwork into isolated transparent PNG layers, inpainted text-free backgrounds, and multi-layer Adobe Photoshop (`.psd`) binary projects.

---

## 🌟 Key Features

### 1. 🎯 Topology-Aware Cutout Engine (BiRefNet & SAM 2)
- **BiRefNet (Bilateral Reference Network)**: Uses fine visual detail and bilateral reference attention to accurately clip **enclosed inner loops and negative spaces** (e.g. inner necks in numbers `2`, `8`, `0`, `6`, letters `B`, `R`, and mug handles).
- **SAM 2 (Segment Anything 2)** & **U2Net**: Multi-model fallback architecture for subject segmentation.

### 2. ⚡ Progressive Multi-Pass Generative Fill (1X to 6X Potency)
- **1X (Standard)**: Fast single-pass background inpainting.
- **2X (Enhanced)** / **4X (High Detail)** / **6X (Max Quality)**: Progressive multi-pass Navier-Stokes & Telea structural continuation with automatic color-matched inner loop punching.

### 3. 📄 Native Adobe Photoshop `.psd` Multi-Layer Exporter
- Assembles extracted RGBA PNG layers (`background_no_text.png`, `subjects.png`, `text_mask.png`, `isolated_objects.png`) into a 100% standard binary `.psd` file compatible with **Adobe Photoshop**, **Photopea**, **GIMP**, and **Affinity Photo**.

### 4. 🎨 Figma-Styled Native 3-Tab Switcher
- **Studio Workspace**: Primary drag-and-drop workspace, live percentage progress, potency selector, and layer cards with click-to-open in Windows Photos.
- **AI Terminal Logs**: Embedded monospace console viewing live launcher, dependency installation, and model downloading (`BiRefNet`, `SAM 2`, `YOLO-World v2`, `Grounding DINO`).
- **Backend Server Logs**: Embedded monospace console streaming live FastAPI (Uvicorn) logs.
- **Zero Popup CMD Windows**: Completely eliminates external console windows!

### 5. 🛠️ Localcel Native Dependency & Environment Manager
- Built-in C# environment manager that detects and installs Python 3.12 via Winget, sets up the virtual environment (`backend/FigPin`), installs PyTorch CUDA binaries, and downloads model weights with an in-app progress dialog.

---

## 📦 Microsoft Store & Installation Details

- **Publisher**: `edwinjoseph` (`CN=808D8BE3-D2A5-4CF6-9F6D-0B5358B6FE53`)
- **Package Identity**: `edwinjoseph.FigPin`
- **Store ID**: `9ND5QL4M5T5R`
- **Package Family Name (PFN)**: `edwinjoseph.FigPin_5kzgk9320a97a`

---

## 💻 Tech Stack

- **Frontend**: C# / WinUI 3 (Windows App SDK 1.6), XAML, Mica Backdrops, Win32 WinRT Interop.
- **Backend**: Python 3.12, FastAPI, Uvicorn, PyTorch (CUDA 12.1), ONNX Runtime GPU.
- **AI Models**: BiRefNet, Segment Anything 2 (SAM 2), U2Net, YOLO-World v2, Grounding DINO Swin-T, EasyOCR.
- **Image Processing**: OpenCV, NumPy, Pillow, Pytoshop.

---

## 🚀 Quick Start (Local Run)

1. Clone the repository:
   ```bash
   git clone https://github.com/edwinjosephshiju/FigPin.git
   cd FigPin
   ```

2. Run the application:
   ```cmd
   run.bat
   ```

---

## 📜 License

Distributed under the **MIT License**. See `LICENSE` for more information.
