# Eagerly copy formats before changing the clipboard. Never retain a delayed
# IDataObject proxy across application shutdown and call it during OleFlushClipboard.
function New-EditorClipboardSnapshot {
    $original = [Windows.Forms.Clipboard]::GetDataObject()
    if (-not $original) { return $null }
    $snapshot = [Windows.Forms.DataObject]::new()
    foreach ($format in $original.GetFormats($false)) {
        $value = $original.GetData($format, $false)
        if ($null -eq $value -and $format -eq [Windows.Forms.DataFormats]::Bitmap) {
            # WinRT image owners can advertise Bitmap while providing its PNG/DIB
            # conversion instead. Materialize that standard image before testing.
            $value = [Windows.Forms.Clipboard]::GetImage()
        }
        if ($null -eq $value) { throw "クリップボード形式を事前複製できないため検証を中止します: $format" }
        if ($value -is [IO.Stream]) {
            if (-not $value.CanSeek) { throw '複製できないストリームのため検証を中止します。' }
            $sourceStream = $value; $position = $sourceStream.Position
            try { $sourceStream.Position = 0; $copy = [IO.MemoryStream]::new(); $sourceStream.CopyTo($copy); $copy.Position = 0; $value = $copy }
            finally { $sourceStream.Position = $position }
        } elseif ($value -is [ICloneable]) { $value = $value.Clone() }
        elseif ($value -isnot [string] -and -not $value.GetType().IsValueType) { throw "安全に複製できない形式のため検証を中止します: $format" }
        $snapshot.SetData($format, $false, $value)
    }
    return $snapshot
}
