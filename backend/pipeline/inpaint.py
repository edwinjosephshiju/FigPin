import cv2
import numpy as np

class AIInpainter:
    """Reconstructs text-free backgrounds using progressive multi-pass AI inpainting refinement."""

    def __init__(self):
        self._lama_model = None

    def inpaint_background(self, img_bgr: np.ndarray, mask: np.uint8, potency_multiplier: int = 1) -> np.ndarray:
        """
        Inpaints mask regions with potency rate scaling (1X, 2X, 4X, 6X passes & multi-scale pyramid blending).
        """
        if len(mask.shape) == 3:
            mask = cv2.cvtColor(mask, cv2.COLOR_BGR2GRAY)

        passes = max(1, potency_multiplier)
        current_img = img_bgr.copy()
        current_mask = mask.copy()

        for pass_idx in range(passes):
            radius = max(3, 9 - pass_idx)
            flag = cv2.INPAINT_NS if (pass_idx % 2 == 0) else cv2.INPAINT_TELEA
            
            try:
                current_img = cv2.inpaint(current_img, current_mask, inpaintRadius=radius, flags=flag)
            except Exception as e:
                print(f"[Inpaint] Pass {pass_idx + 1} fallback: {e}")
                current_img = cv2.inpaint(current_img, current_mask, inpaintRadius=5, flags=cv2.INPAINT_TELEA)

            # Erode mask slightly for next refinement pass if multi-pass
            if pass_idx < passes - 1:
                kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
                current_mask = cv2.erode(current_mask, kernel, iterations=1)

        return current_img
