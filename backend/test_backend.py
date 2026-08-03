import os
import sys

# Add backend directory to sys.path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from pipeline import process_poster

def test_pipeline():
    sample_image = os.path.abspath("../FigPin.png")
    output_dir = os.path.abspath("test_output")
    
    if not os.path.exists(sample_image):
        print(f"Sample image not found at {sample_image}")
        return

    print(f"Testing pipeline with sample image: {sample_image}")
    try:
        manifest = process_poster(sample_image, output_dir)
        print("Pipeline Execution Succeeded!")
        print("Manifest Output:")
        print(manifest)
    except Exception as e:
        print(f"Pipeline Error: {e}")

if __name__ == "__main__":
    test_pipeline()
