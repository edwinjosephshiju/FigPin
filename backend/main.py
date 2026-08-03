import os
import shutil
import uuid
from typing import Dict
from fastapi import FastAPI, UploadFile, File, Form, HTTPException, BackgroundTasks
from fastapi.staticfiles import StaticFiles
from fastapi.middleware.cors import CORSMiddleware

from pipeline.models import JobStatus, ProcessingStage
from pipeline.orchestrator import DeconstructionPipeline

app = FastAPI(title="FigPin Layer Separator Studio API", version="2.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Output directory saved directly into User's Downloads folder under 'FigPin outputs'
USER_DOWNLOADS_DIR = os.path.join(os.path.expanduser("~"), "Downloads")
OUTPUT_DIR = os.path.join(USER_DOWNLOADS_DIR, "FigPin outputs")
os.makedirs(OUTPUT_DIR, exist_ok=True)

app.mount("/output", StaticFiles(directory=OUTPUT_DIR), name="output")

pipeline = DeconstructionPipeline()
jobs_store: Dict[str, JobStatus] = {}

@app.get("/health")
def health_check():
    gpu_available = False
    device_name = "CPU"
    try:
        import torch
        if torch.cuda.is_available():
            gpu_available = True
            device_name = torch.cuda.get_device_name(0)
    except Exception:
        pass
    return {
        "status": "ok", 
        "gpu_available": gpu_available, 
        "device_name": device_name,
        "version": "2.0.0"
    }

def run_pipeline_task(job_id: str, input_path: str, job_dir: str, potency_multiplier: int = 1):
    def progress_callback(percent: int, stage: ProcessingStage, desc: str):
        if job_id in jobs_store:
            job = jobs_store[job_id]
            job.progress_percent = percent
            job.stage = stage
            job.stage_description = desc
            if stage == ProcessingStage.COMPLETED:
                job.status = "completed"
            elif stage == ProcessingStage.FAILED:
                job.status = "failed"

    try:
        result_status = pipeline.process_poster(input_path, job_dir, progress_cb=progress_callback, potency_multiplier=potency_multiplier)
        jobs_store[job_id] = result_status
    except Exception as e:
        print(f"[API Job {job_id}] Execution error: {e}")
        if job_id in jobs_store:
            jobs_store[job_id].status = "failed"
            jobs_store[job_id].stage = ProcessingStage.FAILED
            jobs_store[job_id].stage_description = f"Error: {str(e)}"
            jobs_store[job_id].error_message = str(e)

@app.post("/analyze")
async def analyze_poster(
    background_tasks: BackgroundTasks, 
    file: UploadFile = File(...),
    potency_multiplier: int = Form(1)
):
    job_id = str(uuid.uuid4())[:8]
    job_dir = os.path.join(OUTPUT_DIR, job_id)
    os.makedirs(job_dir, exist_ok=True)

    input_filename = file.filename or "poster.png"
    input_path = os.path.join(job_dir, input_filename)

    with open(input_path, "wb") as buffer:
        shutil.copyfileobj(file.file, buffer)

    initial_job = JobStatus(
        job_id=job_id,
        status="processing",
        stage=ProcessingStage.INITIALIZING,
        progress_percent=0,
        stage_description=f"Initializing {potency_multiplier}X potency GPU execution..."
    )
    jobs_store[job_id] = initial_job

    background_tasks.add_task(run_pipeline_task, job_id, input_path, job_dir, potency_multiplier)
    return {"status": "processing", "job_id": job_id}

@app.get("/status/{job_id}", response_model=JobStatus)
def get_job_status(job_id: str):
    if job_id not in jobs_store:
        raise HTTPException(status_code=404, detail=f"Job ID '{job_id}' not found.")
    return jobs_store[job_id]

@app.post("/separate-layers")
async def separate_layers_shortcut(file: UploadFile = File(...), potency_multiplier: int = Form(1)):
    job_id = str(uuid.uuid4())[:8]
    job_dir = os.path.join(OUTPUT_DIR, job_id)
    os.makedirs(job_dir, exist_ok=True)

    input_filename = file.filename or "poster.png"
    input_path = os.path.join(job_dir, input_filename)

    with open(input_path, "wb") as buffer:
        shutil.copyfileobj(file.file, buffer)

    try:
        result_status = pipeline.process_poster(input_path, job_dir, potency_multiplier=potency_multiplier)
        return result_status.dict()
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Layer separation failed: {str(e)}")

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8000)
