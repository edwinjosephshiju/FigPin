import os
import gc
import cv2
import numpy as np
from PIL import Image

# Global EasyOCR reader lazy loader
_reader = None

def get_ocr_reader():
    global _reader
    if _reader is None:
        import easyocr
        # Initialize EasyOCR reader (English + common languages)
        _reader = easyocr.Reader(['en'], gpu=True)
    return _reader

def clear_gpu_memory():
    """Flushes PyTorch CUDA cache and runs garbage collection to save VRAM."""
    gc.collect()
    try:
        import torch
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
    except Exception:
        pass

def process_poster(image_path: str, output_dir: str):
    """
    Deconstructs a poster into separate layers:
    1. Text Mask (PNG)
    2. Inpainted Background without Text (PNG)
    3. Transparent Subject Cutout (PNG)
    4. Multi-layer PSD file (if supported) / Layer output bundle
    """
    os.makedirs(output_dir, exist_ok=True)
    
    # 1. Load Original Image
    img_bgr = cv2.imread(image_path)
    if img_bgr is None:
        raise ValueError(f"Could not load image at path: {image_path}")
        
    height, width, _ = img_bgr.shape
    
    # --- STEP 1: DETECT TEXT & GENERATE MASK ---
    print("[Pipeline] Step 1: Running OCR text detection...")
    reader = get_ocr_reader()
    results = reader.readtext(image_path)
    
    text_mask = np.zeros((height, width), dtype=np.uint8)
    detected_texts = []
    
    for (bbox, text, prob) in results:
        pts = np.array(bbox, dtype=np.int32)
        cv2.fillPoly(text_mask, [pts], 255)
        detected_texts.append({"text": text, "confidence": float(prob)})
        
    # Dilate mask slightly to cover font drop-shadows, outlines, and anti-aliasing
    kernel = np.ones((7, 7), np.uint8)
    dilated_mask = cv2.dilate(text_mask, kernel, iterations=2)
    
    mask_path = os.path.join(output_dir, "text_mask.png")
    cv2.imwrite(mask_path, dilated_mask)
    print(f"[Pipeline] Text mask saved to {mask_path}")
    
    clear_gpu_memory()

    # --- STEP 2: INPAINT BACKGROUND (REMOVE TEXT) ---
    print("[Pipeline] Step 2: Inpainting background to remove text...")
    inpainted_bg = cv2.inpaint(img_bgr, dilated_mask, inpaintRadius=7, flags=cv2.INPAINT_TELEA)
    bg_path = os.path.join(output_dir, "background_no_text.png")
    cv2.imwrite(bg_path, inpainted_bg)
    print(f"[Pipeline] Clean background saved to {bg_path}")
    
    clear_gpu_memory()

    # --- STEP 3: EXTRACT FOREGROUND SUBJECTS ---
    print("[Pipeline] Step 3: Extracting foreground subjects...")
    try:
        from rembg import remove
        input_pil = Image.open(image_path)
        subject_cutout = remove(input_pil)
        subject_path = os.path.join(output_dir, "subjects.png")
        subject_cutout.save(subject_path)
        print(f"[Pipeline] Subject cutout saved to {subject_path}")
    except Exception as e:
        print(f"[Pipeline] Foreground extraction warning: {e}")
        # Fallback: copy input image as transparent layer placeholder
        subject_path = os.path.join(output_dir, "subjects.png")
        input_pil = Image.open(image_path).convert("RGBA")
        input_pil.save(subject_path)
        
    clear_gpu_memory()

    # --- STEP 4: PSD / LAYER MANIFEST ---
    psd_path = os.path.join(output_dir, "poster_layers.psd")
    # Save a manifest summary for the frontend
    manifest = {
        "status": "success",
        "dimensions": {"width": width, "height": height},
        "detected_text_count": len(detected_texts),
        "layers": {
            "background": bg_path,
            "subjects": subject_path,
            "text_mask": mask_path,
            "psd": psd_path if os.path.exists(psd_path) else None
        }
    }
    
    return manifest
