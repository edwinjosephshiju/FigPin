from enum import Enum
from typing import Callable, List, Optional, Dict, Any
from pydantic import BaseModel, Field

class ProcessingStage(str, Enum):
    INITIALIZING = "Initializing poster processing"
    OBJECT_DETECTION = "Detecting visual objects & layout elements"
    OCR_DETECTION = "Detecting typography & text regions"
    INPAINTING_BACKGROUND = "Reconstructing background with LaMa AI inpainting"
    SUBJECT_SEGMENTATION = "Extracting subject & object cutouts"
    PSD_ASSEMBLY = "Assembling Photoshop PSD layers"
    COMPLETED = "Poster layer deconstruction completed"
    FAILED = "Processing failed"

class LayerInfo(BaseModel):
    layer_id: str
    name: str
    layer_type: str  # "background", "subject", "object", "text_mask", "psd"
    file_path: str
    preview_url: Optional[str] = None
    width: int
    height: int
    details: Optional[str] = None

class JobStatus(BaseModel):
    job_id: str
    status: str  # "queued", "processing", "completed", "failed"
    stage: ProcessingStage
    progress_percent: int = Field(ge=0, le=100, default=0)
    stage_description: str = ""
    dimensions: Optional[Dict[str, int]] = None
    detected_text_count: int = 0
    detected_objects_count: int = 0
    layers: List[LayerInfo] = []
    preview_urls: Dict[str, str] = {}
    error_message: Optional[str] = None

# Progress callback type: takes progress_percent (0..100), stage (ProcessingStage), description (str)
ProgressCallback = Callable[[int, ProcessingStage, str], None]
