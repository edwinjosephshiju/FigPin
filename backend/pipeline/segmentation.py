import os
import cv2
import numpy as np
from PIL import Image

class SubjectSegmenter:
    """Extracts foreground subjects using BiRefNet (topology-aware) with inner hole cleanup."""

    _birefnet_session = None
    _u2net_session = None

    def _get_birefnet_session(self):
        """Lazy-load BiRefNet session (downloads on first use)."""
        if SubjectSegmenter._birefnet_session is None:
            try:
                from rembg import new_session
                print("[Segmentation] Loading BiRefNet model (topology-aware, handles inner loops)...")
                SubjectSegmenter._birefnet_session = new_session("birefnet-general")
                print("[Segmentation] BiRefNet session ready.")
            except Exception as e:
                print(f"[Segmentation] BiRefNet session failed: {e}")
        return SubjectSegmenter._birefnet_session

    def _get_u2net_session(self):
        """Lazy-load fallback U2Net session."""
        if SubjectSegmenter._u2net_session is None:
            try:
                from rembg import new_session
                SubjectSegmenter._u2net_session = new_session("u2net")
            except Exception as e:
                print(f"[Segmentation] U2Net session failed: {e}")
        return SubjectSegmenter._u2net_session

    def _clean_enclosed_holes(self, cutout_pil: Image.Image, potency_multiplier: int) -> Image.Image:
        """
        Post-process pass: punches out any remaining enclosed interior holes
        (inner loops in 2, 8, 0, 6, B, R, mug handles, etc.) using flood-fill topology analysis.
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

        # Filter out tiny noise (< 50px²)
        hole_contours, _ = cv2.findContours(interior_holes, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        final_hole_mask = np.zeros((h, w), dtype=np.uint8)
        for cnt in hole_contours:
            if cv2.contourArea(cnt) > 50:
                cv2.drawContours(final_hole_mask, [cnt], -1, 255, -1)

        # At higher potency, dilate slightly to ensure full edge clean clip
        if potency_multiplier > 1 and np.any(final_hole_mask):
            kernel_size = min(7, 2 + potency_multiplier)
            kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (kernel_size, kernel_size))
            final_hole_mask = cv2.dilate(final_hole_mask, kernel, iterations=1)

        if np.any(final_hole_mask):
            cleansed_alpha = alpha.copy()
            cleansed_alpha[final_hole_mask == 255] = 0
            cutout_rgba[:, :, 3] = cleansed_alpha
            print(f"[Segmentation] Interior hole cleanup: punched {len(hole_contours)} enclosed hole regions.")

        return Image.fromarray(cutout_rgba)

    def extract_subject(self, image_path: str, output_path: str, potency_multiplier: int = 1):
        """
        Extracts subject cutout using:
        - 1X potency: BiRefNet (best zero-shot topology model)
        - 2X+ potency: BiRefNet + alpha matting + topology hole cleanup
        """
        try:
            from rembg import remove
            input_pil = Image.open(image_path)

            # Always prefer BiRefNet — it natively handles inner loops and fine edges
            session = self._get_birefnet_session()

            if session is not None:
                if potency_multiplier > 1:
                    # High potency: BiRefNet + alpha matting for ultra-precise edge detail
                    erode_size = min(30, 10 * potency_multiplier)
                    print(f"[Segmentation] BiRefNet + alpha matting (erode_size={erode_size}, {potency_multiplier}X potency)...")
                    subject_cutout = remove(
                        input_pil,
                        session=session,
                        alpha_matting=True,
                        alpha_matting_foreground_threshold=240,
                        alpha_matting_background_threshold=10,
                        alpha_matting_erode_size=erode_size
                    )
                else:
                    print("[Segmentation] BiRefNet 1X standard pass...")
                    subject_cutout = remove(input_pil, session=session)
            else:
                # Fallback to U2Net with alpha matting if BiRefNet not available
                print("[Segmentation] BiRefNet unavailable, falling back to U2Net...")
                fallback_session = self._get_u2net_session()
                if potency_multiplier > 1:
                    erode_size = min(30, 10 * potency_multiplier)
                    subject_cutout = remove(
                        input_pil,
                        session=fallback_session,
                        alpha_matting=True,
                        alpha_matting_foreground_threshold=240,
                        alpha_matting_background_threshold=10,
                        alpha_matting_erode_size=erode_size
                    )
                else:
                    subject_cutout = remove(input_pil, session=fallback_session)

            # Post-process topology pass: punch out any remaining enclosed interior holes
            cleansed_cutout = self._clean_enclosed_holes(subject_cutout, potency_multiplier)
            cleansed_cutout.save(output_path)
            print(f"[Segmentation] Subject extracted successfully ({potency_multiplier}X potency) -> {output_path}")

        except Exception as e:
            print(f"[Segmentation] Extraction error: {e}")
            try:
                # Last resort: plain U2Net no session
                from rembg import remove
                input_pil = Image.open(image_path)
                subject_cutout = remove(input_pil)
                self._clean_enclosed_holes(subject_cutout, 1).save(output_path)
                print(f"[Segmentation] Fallback extraction completed -> {output_path}")
            except Exception as e2:
                print(f"[Segmentation] All extraction methods failed: {e2}")
                Image.open(image_path).convert("RGBA").save(output_path)
