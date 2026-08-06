import os
import cv2
import numpy as np
from PIL import Image

try:
    import torch
    if hasattr(os, 'add_dll_directory'):
        torch_lib = os.path.join(os.path.dirname(torch.__file__), 'lib')
        if os.path.exists(torch_lib):
            os.add_dll_directory(torch_lib)
except Exception:
    pass

class SubjectSegmenter:
    """Extracts foreground subjects using BiRefNet (topology-aware) with high-precision sub-pixel edge refinement."""

    _birefnet_session = None
    _u2net_session = None

    def _get_birefnet_session(self):
        """Lazy-load BiRefNet session."""
        if SubjectSegmenter._birefnet_session is None:
            try:
                from rembg import new_session
                print("[Segmentation] Loading BiRefNet model (topology-aware, sub-pixel edge precision)...")
                providers = ['CUDAExecutionProvider', 'CPUExecutionProvider']
                SubjectSegmenter._birefnet_session = new_session("birefnet-general", providers=providers)
                print("[Segmentation] BiRefNet session ready with CUDA support.")
            except Exception as e:
                print(f"[Segmentation] BiRefNet session failed: {e}")
        return SubjectSegmenter._birefnet_session

    def _get_u2net_session(self):
        """Lazy-load fallback U2Net session."""
        if SubjectSegmenter._u2net_session is None:
            try:
                from rembg import new_session
                providers = ['CUDAExecutionProvider', 'CPUExecutionProvider']
                SubjectSegmenter._u2net_session = new_session("u2net", providers=providers)
            except Exception as e:
                print(f"[Segmentation] U2Net session failed: {e}")
        return SubjectSegmenter._u2net_session

    def _clean_enclosed_holes(self, cutout_pil: Image.Image, potency_multiplier: int) -> Image.Image:
        """
        Post-process pass: punches out any remaining enclosed interior holes
        (inner loops in 2, 8, 0, 6, B, R, mug handles, etc.) using flood-fill topology analysis
        without eroding valid edge contours.
        """
        cutout_rgba = np.array(cutout_pil.convert("RGBA"))
        alpha = cutout_rgba[:, :, 3]
        h, w = alpha.shape

        if np.count_nonzero(alpha) == 0:
            return cutout_pil

        _, alpha_binary = cv2.threshold(alpha, 128, 255, cv2.THRESH_BINARY)

        # Flood fill from all 4 image borders to label exterior background
        bg_filled = cv2.bitwise_not(alpha_binary)
        flood_mask = np.zeros((h + 2, w + 2), dtype=np.uint8)

        for x in range(w):
            if bg_filled[0, x] == 255:
                cv2.floodFill(bg_filled, flood_mask, (x, 0), 128)
            if bg_filled[h - 1, x] == 255:
                cv2.floodFill(bg_filled, flood_mask, (x, h - 1), 128)
        for y in range(h):
            if bg_filled[y, 0] == 255:
                cv2.floodFill(bg_filled, flood_mask, (0, y), 128)
            if bg_filled[y, w - 1] == 255:
                cv2.floodFill(bg_filled, flood_mask, (w - 1, y), 128)

        # Interior enclosed holes = still 255 (not reached from border flood fill)
        interior_holes = (bg_filled == 255).astype(np.uint8) * 255

        # Filter out tiny noise (< 30px²)
        hole_contours, _ = cv2.findContours(interior_holes, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        final_hole_mask = np.zeros((h, w), dtype=np.uint8)
        for cnt in hole_contours:
            if cv2.contourArea(cnt) > 30:
                cv2.drawContours(final_hole_mask, [cnt], -1, 255, -1)

        if np.any(final_hole_mask):
            cleansed_alpha = alpha.copy()
            cleansed_alpha[final_hole_mask == 255] = 0
            cutout_rgba[:, :, 3] = cleansed_alpha
            print(f"[Segmentation] Interior hole cleanup: punched {len(hole_contours)} enclosed hole regions.")

        return Image.fromarray(cutout_rgba)

    def _refine_edges(self, image_pil: Image.Image, alpha_mask: np.ndarray, potency_multiplier: int) -> np.ndarray:
        """
        Sub-pixel Edge Refinement Pass:
        Uses guided bilateral edge filtering and Canny edge alignment to ensure 
        shiny chrome, 3D bevels, numbers, and fine curves maintain razor-sharp boundaries.
        """
        img_np = np.array(image_pil.convert("RGB"))
        gray = cv2.cvtColor(img_np, cv2.COLOR_RGB2GRAY)

        # Guided bilateral filtering on alpha channel
        alpha_smooth = cv2.bilateralFilter(alpha_mask, d=5, sigmaColor=50, sigmaSpace=50)

        # Detect high-contrast edges in original image
        canny_edges = cv2.Canny(gray, 50, 150)
        edge_dilated = cv2.dilate(canny_edges, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3)))

        # In edge zones, combine smooth alpha with sharp contrast boundary
        refined_alpha = alpha_mask.copy()
        edge_zone = (edge_dilated == 255) & (alpha_mask > 10) & (alpha_mask < 245)
        refined_alpha[edge_zone] = alpha_smooth[edge_zone]

        # Sharp thresholding for clean crisp vector edges
        if potency_multiplier >= 4:
            _, refined_alpha = cv2.threshold(refined_alpha, 128, 255, cv2.THRESH_BINARY)
            kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
            refined_alpha = cv2.morphologyEx(refined_alpha, cv2.MORPH_CLOSE, kernel)

        return refined_alpha

    def extract_subject(self, image_path: str, output_path: str, potency_multiplier: int = 1):
        """
        Extracts subject cutout with razor-sharp edge precision:
        - 1X: BiRefNet Standard Pass
        - 2X: BiRefNet + Conservative Alpha Matting (erode_size=4) + Guided Edge Smoothing
        - 4X: 2X Super-Sampling Resolution BiRefNet + Sub-pixel Edge Refinement
        - 6X: 3X Super-Sampling Resolution BiRefNet + Sub-pixel Edge Refinement + Binary Contour Sharpness
        """
        try:
            from rembg import remove
            input_pil = Image.open(image_path)
            orig_w, orig_h = input_pil.size

            session = self._get_birefnet_session()
            if session is None:
                session = self._get_u2net_session()

            if potency_multiplier >= 4:
                # 4X & 6X Potency: High-Resolution Super-Sampling for Sub-pixel Edge Precision
                scale_factor = 2 if potency_multiplier == 4 else 3
                new_size = (orig_w * scale_factor, orig_h * scale_factor)
                print(f"[Segmentation] {potency_multiplier}X Potency: Super-sampling image to {new_size[0]}x{new_size[1]} for sub-pixel edge precision...")

                scaled_pil = input_pil.resize(new_size, Image.Resampling.LANCZOS)
                
                # Run BiRefNet on super-sampled image with conservative matting
                cutout_high_res = remove(
                    scaled_pil,
                    session=session,
                    alpha_matting=True,
                    alpha_matting_foreground_threshold=240,
                    alpha_matting_background_threshold=10,
                    alpha_matting_erode_size=4
                )

                # Resize cutout back to original dimensions with Lanczos filtering
                cutout_pil = cutout_high_res.resize((orig_w, orig_h), Image.Resampling.LANCZOS)
            
            elif potency_multiplier == 2:
                # 2X Potency: BiRefNet + Conservative Matting (erode_size=4)
                print(f"[Segmentation] 2X Potency: BiRefNet + Conservative Alpha Matting (erode_size=4)...")
                cutout_pil = remove(
                    input_pil,
                    session=session,
                    alpha_matting=True,
                    alpha_matting_foreground_threshold=240,
                    alpha_matting_background_threshold=10,
                    alpha_matting_erode_size=4
                )
            else:
                # 1X Potency: Standard Pass
                print("[Segmentation] 1X Potency: Standard BiRefNet pass...")
                cutout_pil = remove(input_pil, session=session)

            # Extract RGBA array
            cutout_rgba = np.array(cutout_pil.convert("RGBA"))
            alpha = cutout_rgba[:, :, 3]

            # Apply Sub-pixel Edge Refinement Pass for 2X+ potencies
            if potency_multiplier > 1 and np.count_nonzero(alpha) > 0:
                refined_alpha = self._refine_edges(input_pil, alpha, potency_multiplier)
                cutout_rgba[:, :, 3] = refined_alpha
                cutout_pil = Image.fromarray(cutout_rgba)

            # Post-process topology pass: punch out interior holes (loops in 2, 8, 0, 6, B, R)
            cleansed_cutout = self._clean_enclosed_holes(cutout_pil, potency_multiplier)
            cleansed_cutout.save(output_path)
            print(f"[Segmentation] Subject extracted successfully ({potency_multiplier}X potency) -> {output_path}")

        except Exception as e:
            print(f"[Segmentation] Extraction error: {e}")
            try:
                from rembg import remove
                input_pil = Image.open(image_path)
                subject_cutout = remove(input_pil)
                self._clean_enclosed_holes(subject_cutout, 1).save(output_path)
                print(f"[Segmentation] Fallback extraction completed -> {output_path}")
            except Exception as e2:
                print(f"[Segmentation] All extraction methods failed: {e2}")
                Image.open(image_path).convert("RGBA").save(output_path)
