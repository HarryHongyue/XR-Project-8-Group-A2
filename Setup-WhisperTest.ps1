# Verifies the Whisper key actually authorizes: a POST without an audio file
# should get a 4xx (validation) rather than 401/403 (auth failure).
$k = [Environment]::GetEnvironmentVariable('WHISPER_API_KEY','User')
curl.exe -s -o NUL -w "no-file POST -> %{http_code}`n" -H "Authorization: Bearer $k" -F model=Systran/faster-whisper-small https://whisper.omnigent.nl/v1/audio/transcriptions
