using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.InteropServices;

namespace BlazorOllamaStudio.Data;

public class DocumentoFragmento
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(250)]
    public string NombreArchivo { get; set; } = string.Empty;

    public int NumeroPagina { get; set; }

    [Required]
    public string Contenido { get; set; } = string.Empty;

    public DateTime FechaIndexado { get; set; } = DateTime.UtcNow;

    // Almacenamiento binario compatible con SQL Server LocalDB (2019/2022)
    [Required]
    public byte[] EmbeddingBytes { get; set; } = [];

    [NotMapped]
    public float[] Embedding
    {
        get => MemoryMarshal.Cast<byte, float>(EmbeddingBytes).ToArray();
        set => EmbeddingBytes = MemoryMarshal.AsBytes(value.AsSpan()).ToArray();
    }
}
