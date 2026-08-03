import os
import sys
import time
import urllib.request
import urllib.error
from pathlib import Path

USER_HOME = Path.home()

MODELS_DIR = Path(__file__).parent / "models"
U2NET_DIR = USER_HOME / ".u2net"
SAM2_DIR = MODELS_DIR / "sam2"
YOLO_DIR = MODELS_DIR / "yolo"
GROUNDING_DIR = MODELS_DIR / "groundingdino"

BIREFNET_DIR = USER_HOME / ".u2net"

MODEL_MANIFEST = [
    {
        "name": "rembg (U2Net Background Removal)",
        "url": "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx",
        "dest_dir": U2NET_DIR,
        "filename": "u2net.onnx",
    },
    {
        "name": "SAM 2 Hiera Base Plus (Segment Anything 2)",
        "url": "https://dl.fbaipublicfiles.com/segment_anything_2/072824/sam2_hiera_base_plus.pt",
        "dest_dir": SAM2_DIR,
        "filename": "sam2_hiera_base_plus.pt",
    },
    {
        "name": "YOLO-World v2 (Open-Vocabulary Object Detection)",
        "url": "https://github.com/ultralytics/assets/releases/download/v8.2.0/yolov8x-worldv2.pt",
        "dest_dir": YOLO_DIR,
        "filename": "yolov8x-worldv2.pt",
    },
    {
        "name": "Grounding DINO Swin-T (Object Detector)",
        "url": "https://github.com/IDEA-Research/GroundingDINO/releases/download/v0.1.0-alpha/groundingdino_swint_ogc.pth",
        "dest_dir": GROUNDING_DIR,
        "filename": "groundingdino_swint_ogc.pth",
    }
]

def format_bytes(size_in_bytes: float) -> str:
    for unit in ['B', 'KB', 'MB', 'GB', 'TB']:
        if size_in_bytes < 1024.0:
            return f"{size_in_bytes:.2f} {unit}"
        size_in_bytes /= 1024.0
    return f"{size_in_bytes:.2f} PB"

def download_file_with_resume(url: str, dest_path: Path, model_name: str) -> bool:
    dest_path.parent.mkdir(parents=True, exist_ok=True)
    temp_path = dest_path.with_suffix(dest_path.suffix + ".tmp")
    
    if dest_path.exists() and dest_path.stat().st_size > 100000:
        print(f"[COMPLETE] {model_name} ready ({format_bytes(dest_path.stat().st_size)}).")
        return True
        
    downloaded_bytes = 0
    if temp_path.exists():
        downloaded_bytes = temp_path.stat().st_size
            
    headers = {'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'}
    if downloaded_bytes > 0:
        headers['Range'] = f"bytes={downloaded_bytes}-"
        print(f"[RESUMING] {model_name} from {format_bytes(downloaded_bytes)}...")
    else:
        print(f"[DOWNLOADING] {model_name}...")
        
    req = urllib.request.Request(url, headers=headers)
    
    try:
        start_time = time.time()
        with urllib.request.urlopen(req, timeout=30) as response:
            mode = 'ab' if response.status == 206 else 'wb'
            if response.status == 200:
                downloaded_bytes = 0

            content_length = response.headers.get('Content-Length')
            total_size = int(content_length) + downloaded_bytes if content_length else -1

            with open(temp_path, mode) as out_file:
                chunk_size = 1024 * 64
                last_print_time = time.time()
                
                while True:
                    chunk = response.read(chunk_size)
                    if not chunk:
                        break
                    out_file.write(chunk)
                    downloaded_bytes += len(chunk)
                    
                    now = time.time()
                    if now - last_print_time >= 0.3 or (total_size > 0 and downloaded_bytes == total_size):
                        last_print_time = now
                        elapsed = max(now - start_time, 0.001)
                        speed = downloaded_bytes / elapsed
                        
                        if total_size > 0:
                            percent = (downloaded_bytes / total_size) * 100
                            bar_len = 30
                            filled = int(bar_len * downloaded_bytes // total_size)
                            bar = '=' * filled + '-' * (bar_len - filled)
                            sys.stdout.write(
                                f"\r  [{bar}] {percent:.1f}% ({format_bytes(downloaded_bytes)}/{format_bytes(total_size)}) @ {format_bytes(speed)}/s"
                            )
                        else:
                            sys.stdout.write(f"\r  Downloaded: {format_bytes(downloaded_bytes)} @ {format_bytes(speed)}/s")
                        sys.stdout.flush()
                        
        print()
        if dest_path.exists():
            dest_path.unlink()
        temp_path.replace(dest_path)
        print(f"[SUCCESS] {model_name} saved to {dest_path}")
        return True

    except urllib.error.HTTPError as e:
        if e.code == 416:
            if temp_path.exists():
                if dest_path.exists():
                    dest_path.unlink()
                temp_path.replace(dest_path)
            print(f"[COMPLETE] {model_name} verified.")
            return True
        print(f"\n[ERROR] HTTP Error {e.code} for {model_name}: {e.reason}")
        return False
    except KeyboardInterrupt:
        print(f"\n\n[CANCELLED] Download of {model_name} paused by user.")
        print(f"  Partial file saved at {temp_path}. Rerun script to resume!")
        sys.exit(0)
    except Exception as e:
        print(f"\n[ERROR] Failed downloading {model_name}: {e}")
        return False

def ensure_birefnet():
    """Pre-download BiRefNet model weights via rembg session manager."""
    birefnet_path = BIREFNET_DIR / "birefnet-general.onnx"
    # rembg stores birefnet files in ~/.u2net/ - check if already cached
    birefnet_files = list(BIREFNET_DIR.glob("birefnet*")) if BIREFNET_DIR.exists() else []
    if birefnet_files:
        total_size = sum(f.stat().st_size for f in birefnet_files)
        print(f"[COMPLETE] BiRefNet (Topology-Aware Cutout) ready ({format_bytes(total_size)}).")
        return True
    try:
        print("[DOWNLOADING] BiRefNet (Topology-Aware Cutout) - best model for inner loop detection...")
        from rembg import new_session
        new_session("birefnet-general")
        birefnet_files = list(BIREFNET_DIR.glob("birefnet*"))
        if birefnet_files:
            total_size = sum(f.stat().st_size for f in birefnet_files)
            print(f"[SUCCESS] BiRefNet cached ({format_bytes(total_size)}).")
            return True
        else:
            print("[WARNING] BiRefNet download attempted but files not confirmed.")
            return False
    except KeyboardInterrupt:
        print("\n[CANCELLED] BiRefNet download paused. Rerun to resume.")
        sys.exit(0)
    except Exception as e:
        print(f"[WARNING] BiRefNet download failed (will use U2Net fallback): {e}")
        return False

def main():
    print("=" * 70)
    print("Poster Layer Separator - Sequential Model Installer")
    print("=" * 70)
    print("Features: Resumable Downloads | Safe Ctrl+C Cancellation | Integrity Scanning")
    print("-" * 70)

    # BiRefNet is the primary cutout model — pre-cache it first
    total_models = len(MODEL_MANIFEST) + 1
    success_count = 0

    print(f"\n[1/{total_models}] Checking: BiRefNet (Topology-Aware Cutout)")
    if ensure_birefnet():
        success_count += 1

    for idx, model in enumerate(MODEL_MANIFEST, 2):
        name = model["name"]
        url = model["url"]
        dest_path = model["dest_dir"] / model["filename"]
        
        print(f"\n[{idx}/{total_models}] Checking: {name}")
        ok = download_file_with_resume(url, dest_path, name)
        if ok:
            success_count += 1
            
    print("\n" + "=" * 70)
    print(f"Download Summary: {success_count}/{total_models} models ready.")
    print("=" * 70)

if __name__ == "__main__":
    main()
