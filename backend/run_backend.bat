@echo off
set VENV_PYTHON=%~dp0FigPin\Scripts\python.exe

if not exist "%VENV_PYTHON%" (
    echo Creating FigPin virtual environment...
    py -3.12 -m venv "%~dp0FigPin"
    "%VENV_PYTHON%" -m pip install -r "%~dp0requirements.txt"
)

echo Starting Poster Layer Separator Python AI Backend (FigPin VENV)...
"%VENV_PYTHON%" -m uvicorn main:app --host 127.0.0.1 --port 8000 --reload
pause
