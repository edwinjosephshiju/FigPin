import os
import gc
import cv2
import numpy as np
from typing import Optional, Dict, Any

from .models import ProcessingStage, LayerInfo, JobStatus, ProgressCallback
from .ocr import OCRProcessor
from .inpaint import AIInpainter
from .segmentation import SubjectSegmenter
from .detector import ObjectDetector
from .psd import PSDExporter

def clear_gpu_memory():
    gc.collect()
    try:
        import torch
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
    except Exception:
        pass

class DeconstructionPipeline:
    """Master orchestrator executing multi-stage poster layer separation."""

    def __init__(self):
        self.ocr_processor = OCRProcessor()
        self.inpainter = AIInpainter()
        self.segmenter = SubjectSegmenter()
        self.detector = ObjectDetector()
        self.psd_exporter = PSDExporter()

    def process_poster(self, image_path: str, output_dir: str, progress_cb: Optional[ProgressCallback] = None, potency_multiplier: int = 1) -> JobStatus:
        os.makedirs(output_dir, exist_ok=True)

        def report_progress(percent: int, stage: ProcessingStage, desc: str):
            if progress_cb:
                progress_cb(percent, stage, desc)

        # STAGE 0: Initializing
        report_progress(5, ProcessingStage.INITIALIZING, f"Loading image ({potency_multiplier}X AI Potency Rate)...")
        img_bgr = cv2.imread(image_path)
        if img_bgr is None:
            raise ValueError(f"Unable to read image at: {image_path}")

        height, width, _ = img_bgr.shape
        job_id = os.path.basename(output_dir)

        # STAGE 1: OCR Text Detection
        report_progress(20, ProcessingStage.OCR_DETECTION, "Stage 1/5: Detecting text regions with OCR...")
        text_mask, detected_texts = self.ocr_processor.detect_text_and_create_mask(image_path, height, width)
        
        mask_path = os.path.join(output_dir, "text_mask.png")
        cv2.imwrite(mask_path, text_mask)
        clear_gpu_memory()

        # STAGE 2: AI Inpainting Background
        report_progress(40, ProcessingStage.INPAINTING_BACKGROUND, f"Stage 2/5: Generative background reconstruction ({potency_multiplier}X multi-pass inpainting)...")
        inpainted_bg = self.inpainter.inpaint_background(img_bgr, text_mask, potency_multiplier=potency_multiplier)
        
        bg_path = os.path.join(output_dir, "background_no_text.png")
        cv2.imwrite(bg_path, inpainted_bg)
        clear_gpu_memory()

        # STAGE 3: Foreground Subject Cutout
        report_progress(65, ProcessingStage.SUBJECT_SEGMENTATION, f"Stage 3/5: Extracting subject cutout ({potency_multiplier}X alpha matting resolution)...")
        subject_path = os.path.join(output_dir, "subjects.png")
        self.segmenter.extract_subject(image_path, subject_path, potency_multiplier=potency_multiplier)
        clear_gpu_memory()

        # STAGE 4: Object & Element Detection
        report_progress(80, ProcessingStage.OBJECT_DETECTION, "Stage 4/5: Isolating graphic elements & objects...")
        detected_objects = self.detector.detect_objects(image_path, text_mask, output_dir)
        clear_gpu_memory()

        # STAGE 5: PSD Assembly & Export
        report_progress(95, ProcessingStage.PSD_ASSEMBLY, "Stage 5/5: Exporting Photoshop PSD project layers...")
        psd_path = os.path.join(output_dir, "poster_layers.psd")
        layer_dict = {"background": bg_path, "subjects": subject_path, "text_mask": mask_path}
        self.psd_exporter.export_psd(layer_dict, psd_path)

        # Build Layer Objects
        base_url = f"http://127.0.0.1:8000/output/{job_id}"
        layers = [
            LayerInfo(
                layer_id="bg",
                name="Inpainted Background (No Text)",
                layer_type="background",
                file_path=bg_path,
                preview_url=f"{base_url}/background_no_text.png",
                width=width,
                height=height,
                details=f"AI Inpainted ({potency_multiplier}X Potency)"
            ),
            LayerInfo(
                layer_id="subjects",
                name="Foreground Subject Cutout",
                layer_type="subject",
                file_path=subject_path,
                preview_url=f"{base_url}/subjects.png",
                width=width,
                height=height,
                details=f"Isolated Subject Cutout ({potency_multiplier}X Matting)"
            ),
            LayerInfo(
                layer_id="text_mask",
                name="OCR Text Mask",
                layer_type="text_mask",
                file_path=mask_path,
                preview_url=f"{base_url}/text_mask.png",
                width=width,
                height=height,
                details=f"Masked {len(detected_texts)} detected text regions"
            )
        ]

        # Add sub-objects
        for obj in detected_objects:
            obj_name = obj["name"]
            obj_file = os.path.basename(obj["path"])
            layers.append(LayerInfo(
                layer_id=obj["id"],
                name=obj_name,
                layer_type="object",
                file_path=obj["path"],
                preview_url=f"{base_url}/{obj_file}",
                width=obj["bbox"][2],
                height=obj["bbox"][3],
                details=f"Isolated element bbox {obj['bbox']}"
            ))

        status = JobStatus(
            job_id=job_id,
            status="completed",
            stage=ProcessingStage.COMPLETED,
            progress_percent=100,
            stage_description=f"Layer separation completed ({potency_multiplier}X Potency)",
            dimensions={"width": width, "height": height},
            detected_text_count=len(detected_texts),
            detected_objects_count=len(detected_objects),
            layers=layers,
            preview_urls={
                "background": f"{base_url}/background_no_text.png",
                "subjects": f"{base_url}/subjects.png",
                "text_mask": f"{base_url}/text_mask.png"
            }
        )

        report_progress(100, ProcessingStage.COMPLETED, "Processing completed!")
        return status
