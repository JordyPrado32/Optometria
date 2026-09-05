# Laboratorio Visual: revisión local

Vista de desarrollo de los componentes reales, sin autenticación y sin depender del arranque de SQL Server. No desplegar este proyecto como aplicación pública. El catálogo no tiene datos en esta vista.

```powershell
dotnet run --project tools/VisualLabPreview -- --urls http://localhost:5066
```

Laboratorio: http://localhost:5066/ · Test: http://localhost:5066/?test=true

La aplicación principal conserva sus rutas `/vision-simulator` y `/visual-acuity-test`, navegación y configuración de base de datos.

## Alcance y limitaciones

- Escenas 3D procedurales, no modelos anatómicos clínicos ni medidas ópticas certificadas. Three.js y OrbitControls se cargan desde CDN al abrir una escena.
- Probador con cámara local y alineación manual; no implementa seguimiento facial automático ni medición pupilar. Requiere permiso y contexto seguro o localhost.
- Test educativo: cinco respuestas por nivel, cuatro aciertos para avanzar, tres condiciones de evaluación (ambos ojos, derecho, izquierdo). Requiere calibración física; limita niveles inferiores a cinco píxeles. Los glifos no constituyen una cartilla clínica estandarizada.
- Referencia geométrica de cinco minutos de arco: [Webvision, Visual Acuity](https://www.ncbi.nlm.nih.gov/books/NBK11509/).
- Las imágenes de calle y paisaje fueron generadas específicamente para esta interfaz con ImageGen. Lectura y pantalla son SVG originales. No representan resultados clínicos ni fotografías de pacientes.

## Comprobación manual

Cambiar módulos y condiciones; modificar corrección, capas y vista; probar las tres formas y tratamientos; arrastrar y usar teclado en el comparador; recorrer calibración y tres ojos con respuestas incorrectas y correctas; revisar anchuras de escritorio y móvil. Probar permiso de cámara denegado y concedido en el equipo del usuario.
