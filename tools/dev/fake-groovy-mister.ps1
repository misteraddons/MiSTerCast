param(
    [string]$Address = "127.0.0.1",
    [int]$Port = 32100,
    [switch]$Once
)

$endpoint = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Parse($Address), $Port)
$udp = [System.Net.Sockets.UdpClient]::new($endpoint)

try {
    Write-Host "Fake Groovy_MiSTer listening on $Address`:$Port"
    while ($true) {
        $remote = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
        $packet = $udp.Receive([ref]$remote)
        if ($packet.Length -eq 0) {
            continue
        }

        if ($packet[0] -eq 2) {
            $ack = [byte[]]::new(13)
            [BitConverter]::GetBytes([uint32]1).CopyTo($ack, 0)
            [BitConverter]::GetBytes([uint16]120).CopyTo($ack, 4)
            [BitConverter]::GetBytes([uint32]1).CopyTo($ack, 6)
            [BitConverter]::GetBytes([uint16]120).CopyTo($ack, 10)
            $ack[12] = 0x45
            [void]$udp.Send($ack, $ack.Length, $remote)
            Write-Host "ACK sent to $($remote.Address):$($remote.Port)"
            if ($Once) {
                break
            }
        }
        elseif ($packet[0] -eq 1 -and $Once) {
            break
        }
    }
}
finally {
    $udp.Close()
}
