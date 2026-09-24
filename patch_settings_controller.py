import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    new_download_method = '''    [HttpPost("whisper/download")]
    public async Task<IActionResult> DownloadWhisperModel([FromServices] QuimeraReader.Infrastructure.Services.AudioAlignmentQueue queue)
    {
        string modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";
        string targetPath = "ggml-base.bin";

        bool wasAlreadyDownloaded = System.IO.File.Exists(targetPath);

        if (!wasAlreadyDownloaded)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                using var stream = await httpClient.GetStreamAsync(modelUrl);
                using var fileStream = new FileStream(targetPath, FileMode.CreateNew);
                await stream.CopyToAsync(fileStream);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error al descargar el modelo Whisper");
                return StatusCode(500, new { Message = "Error al descargar el modelo: " + ex.Message });
            }
        }

        // Auto-requeue any ERROR books
        var failedBooks = _dbContext.Books.Where(b => b.ProcessingStatus == "ERROR").ToList();
        foreach (var book in failedBooks)
        {
            book.ProcessingStatus = "PENDING_SYNC";
            await queue.EnqueueAsync(book.Id);
        }
        await _dbContext.SaveChangesAsync();

        return Ok(new { Message = "Modelo descargado con éxito y trabajos reanudados." });
    }

    [HttpGet("whisper/status")]
    public IActionResult GetWhisperStatus()
    {
        bool isDownloaded = System.IO.File.Exists("ggml-base.bin");
        return Ok(new { IsDownloaded = isDownloaded });
    }
'''
    # We replace the old DownloadWhisperModel
    import re
    # Match the old DownloadWhisperModel
    old_regex = r'\[HttpPost\("whisper/download"\)\].*?(?=\[HttpPost\("restart"\)\])'
    content = re.sub(old_regex, new_download_method, content, flags=re.DOTALL)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.API/Controllers/SettingsController.cs')