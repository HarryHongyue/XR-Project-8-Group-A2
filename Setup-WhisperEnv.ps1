# One-time setup: pulls the shared Whisper key from the voice-dictation server
# and stores the three env vars the Unity app reads at runtime. The key is never
# written to the repo - it lives only in the user environment.
$key = wsl ssh -i ~/.ssh/devin_omnigent_deploy root@57.131.149.244 "grep '^WHISPER_API_KEY=' /root/voice-dictation/.env | cut -d= -f2 | tr -d '\r\n '" 2>$null
if ($key) {
  [Environment]::SetEnvironmentVariable('WHISPER_BASE_URL','https://whisper.omnigent.nl','User')
  [Environment]::SetEnvironmentVariable('WHISPER_MODEL','Systran/faster-whisper-small','User')
  [Environment]::SetEnvironmentVariable('WHISPER_API_KEY',$key,'User')
  Write-Output ('KEY SET, length=' + $key.Length)
} else { Write-Output 'KEY FETCH FAILED' }
