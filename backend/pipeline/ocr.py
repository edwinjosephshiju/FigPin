import numpy as np
import cv2
import gc
from typing import List, Tuple, Dict, Any

class OCRProcessor:
    """Handles text detection and typography mask generation using PaddleOCR or EasyOCR."""
    
    def __init__(self):
        self._reader = None
        self._engine_name = "EasyOCR"
        
    def _lazy_init(self):
        if self._reader is not None:
            return
            
        # Try PaddleOCR first, fall back to EasyOCR
        try:
            from paddleocr import PaddleOCR
            self._reader = PaddleOCR(use_angle_cls=True, lang='en', show_log=False)
            self._engine_name = "PaddleOCR"
            print("[OCR] Initialized PaddleOCR engine successfully.")
        except Exception as e:
            print(f"[OCR] PaddleOCR not available ({e}), loading EasyOCR fallback...")
            import easyocr
            self._reader = easyocr.Reader(['en'], gpu=True, verbose=False)
            self._engine_name = "EasyOCR"
            print("[OCR] Initialized EasyOCR engine successfully.")

    def detect_text_and_create_mask(self, image_path: str, height: int, width: int) -> Tuple[np.ndarray, List[Dict[str, Any]]]:
        self._lazy_init()
        detected_texts = []
        text_mask = np.zeros((height, width), dtype=np.uint8)
        
        try:
            if self._engine_name == "PaddleOCR":
                result = self._reader.ocr(image_path, cls=True)
                if result and result[0]:
                    for line in result[0]:
                        bbox, (text, prob) = line[0], line[1]
                        pts = np.array(bbox, dtype=np.int32)
                        cv2.fillPoly(text_mask, [pts], 255)
                        detected_texts.append({"text": text, "confidence": float(prob), "bbox": bbox})
            else:
                results = self._reader.readtext(image_path)
                for (bbox, text, prob) in results:
                    pts = np.array(bbox, dtype=np.int32)
                    cv2.fillPoly(text_mask, [pts], 255)
                    detected_texts.append({"text": text, "confidence": float(prob), "bbox": bbox})
        except Exception as e:
            print(f"[OCR] Detection exception: {e}")
            
        kernel = np.ones((7, 7), np.uint8)
        dilated_mask = cv2.dilate(text_mask, kernel, iterations=2)
        
        return dilated_mask, detected_texts
