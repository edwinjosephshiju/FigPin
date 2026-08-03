import cv2
import numpy as np
from PIL import Image
from typing import List, Dict, Any

class ObjectDetector:
    """Analyzes layout contours and open-vocabulary objects to isolate graphical elements."""

    def detect_objects(self, image_path: str, mask: np.ndarray, output_dir: str) -> List[Dict[str, Any]]:
        """
        Extracts sub-objects and returns a list of detected element layers.
        """
        img = cv2.imread(image_path)
        if img is None:
            return []
            
        height, width, _ = img.shape
        object_layers = []

        # Find contours of foreground non-text objects
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        
        obj_count = 0
        for i, cnt in enumerate(contours):
            area = cv2.contourArea(cnt)
            # Filter tiny noise specs
            if area > (width * height * 0.01):
                obj_count += 1
                x, y, w, h = cv2.boundingRect(cnt)
                
                # Crop region and save cutout layer
                crop_mask = np.zeros((height, width), dtype=np.uint8)
                cv2.drawContours(crop_mask, [cnt], -1, 255, -1)
                
                # Create RGBA object cutout
                b, g, r = cv2.split(img)
                alpha = crop_mask
                rgba = cv2.merge([b, g, r, alpha])
                
                obj_file = f"object_{obj_count}.png"
                obj_path = f"{output_dir}/{obj_file}"
                cv2.imwrite(obj_path, rgba[y:y+h, x:x+w])
                
                object_layers.append({
                    "id": f"obj_{obj_count}",
                    "name": f"Graphic Element {obj_count}",
                    "path": obj_path,
                    "bbox": [x, y, w, h]
                })

        return object_layers
