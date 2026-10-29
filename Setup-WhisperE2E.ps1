$k = [Environment]::GetEnvironmentVariable('WHISPER_API_KEY','User')
$wav = "C:\Users\Harry\Desktop\2_ADSAI\eXtended Reality\XR Project 8 Group A2\.tools\test_tone.wav"
Write-Output "=== real model name ==="
curl.exe -s -w "`nHTTP %{http_code}`n" -H "Authorization: Bearer $k" -F "file=@$wav;type=audio/wav" -F "model=Systran/faster-whisper-small" -F "response_format=json" -F "language=en" https://whisper.omnigent.nl/v1/audio/transcriptions
Write-Output "=== fallback model whisper-1 (what Unity sends when env vars are missing) ==="
curl.exe -s -w "`nHTTP %{http_code}`n" -H "Authorization: Bearer $k" -F "file=@$wav;type=audio/wav" -F "model=whisper-1" https://whisper.omnigent.nl/v1/audio/transcriptions
