import os
import numpy as np
from PIL import Image

class PSDExporter:
    """Assembles transparent PNG layers into multi-layer Photoshop PSD format."""

    def export_psd(self, layer_paths: dict, output_psd_path: str):
        """
        Creates a genuine multi-layer Adobe Photoshop (.psd) binary file from generated PNG layers.
        Compatible with Adobe Photoshop, Photopea, GIMP, Affinity Photo, etc.
        """
        try:
            import pytoshop
            from pytoshop.user import nested_layers

            psd_layers = []

            for layer_name, path in layer_paths.items():
                if not os.path.exists(path):
                    continue

                with Image.open(path) as img:
                    rgba_img = img.convert("RGBA")
                    arr = np.array(rgba_img)

                    # Extract RGBA channel arrays
                    r = arr[:, :, 0]
                    g = arr[:, :, 1]
                    b = arr[:, :, 2]
                    a = arr[:, :, 3]

                    channels = {
                        0: r,
                        1: g,
                        2: b,
                        -1: a
                    }

                    layer = nested_layers.Image(
                        name=layer_name.replace("_", " ").title(),
                        visible=True,
                        opacity=255,
                        channels=channels
                    )
                    psd_layers.append(layer)

            if not psd_layers:
                print(f"[PSD] Warning: No valid layer images found for {output_psd_path}")
                return

            psd = nested_layers.nested_layers_to_psd(psd_layers, color_mode=3, compression=0)
            with open(output_psd_path, "wb") as f:
                psd.write(f)

            print(f"[PSD] Successfully exported multi-layer Photoshop PSD ({os.path.getsize(output_psd_path)} bytes) to: {output_psd_path}")

        except Exception as e:
            print(f"[PSD] Exception during PSD export: {e}")
            # Fallback to copy first layer as image if pytoshop error
            for path in layer_paths.values():
                if os.path.exists(path):
                    with Image.open(path) as img:
                        img.save(output_psd_path, format="PSD")
                        break
