"""Saca los rasgos de los dibujos de Ale y Memi para la herramienta de Unity
Tools > Amistad > 4 · Iluminación > Ale y Memi: repintar texturas (hornear).
R = trazos de tinta (líneas oscuras delgadas; se quita el relleno negro grande de la ropa)
G = luces pintadas (el tono durazno claro), A = alfa del dibujo.
Uso (desde la raíz del proyecto):  python Docs/scripts/rasgos_dibujo.py   (necesita numpy y Pillow)
"""
import numpy as np
from PIL import Image, ImageFilter

T = "Assets/Assets/3d/Persoanjes/Texturas_MemiAle/"
for fuente, nombre in [("20260907 TEX A VER SI SALE.png", "Ale"), ("20260907 TEX A VER SI SALE 1.png", "Memi")]:
    a = np.asarray(Image.open(T + fuente).convert("RGBA")).astype(float) / 255
    rgb, al = a[..., :3], a[..., 3]
    lum = rgb @ np.array([0.299, 0.587, 0.114])
    oscuro = Image.fromarray(((lum < 0.2) & (al > 0.5)).astype(np.uint8) * 255)
    relleno = oscuro.filter(ImageFilter.MinFilter(11)).filter(ImageFilter.MaxFilter(15))   # zonas oscuras grandes (ropa)
    linea = np.clip(np.asarray(oscuro).astype(float) - np.asarray(relleno).astype(float), 0, 255)
    linea = np.asarray(Image.fromarray(linea.astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))).astype(float)
    d = np.linalg.norm(rgb - np.array([0.85, 0.6, 0.45]), axis=2)
    luz = np.clip((0.16 - d) / 0.08, 0, 1) * 255 * (al > 0.5)
    luz = np.asarray(Image.fromarray(luz.astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))).astype(float)
    Image.fromarray(np.dstack([linea, luz, np.zeros_like(linea), al * 255]).astype(np.uint8), "RGBA").save(T + f"Rasgos_{nombre}.png")
    print(nombre, "listo")
