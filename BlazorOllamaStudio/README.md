# BlazorOllamaStudio (.NET 10 + OllamaSharp + SQL Server LocalDB)

Aplicación Blazor Server lista para compilar en **.NET 10** que integra:
- **Chat IA en streaming** con citas de origen.
- **RAG Híbrido** (similitud vectorial SIMD `TensorPrimitives` + coincidencia de palabras clave) sobre **SQL Server LocalDB** (`VARBINARY(MAX)`).
- **Ingesta estructurada** de documentos `.docx` (detectando encabezados y tablas), `.pdf`, `.md` y `.txt`.
- **Gestión de modelos Ollama** (descarga directa e importación de archivos `.gguf`).

## Puesta en marcha

1. **Instalar modelos en Ollama:**
   ```bash
   ollama pull bge-m3
   ollama pull qwen3:8b
   ```

2. **Base de datos SQL Server LocalDB:**
   Ya viene preconfigurada en `appsettings.json` apuntando a `Server=(localdb)\mssqllocaldb;Database=BlazorOllamaRag;...`. Al arrancar, la aplicación creará automáticamente el archivo `.mdf` en tu perfil de usuario y la tabla `DocumentoFragmentos`.

3. **Compilar y ejecutar:**
   ```bash
   dotnet restore
   dotnet run
   ```
