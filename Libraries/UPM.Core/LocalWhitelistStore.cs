using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace UPM.Core;

/// <summary>
/// 로컬 파일 시스템에 화이트리스트를 저장/로드하는 저장소.
/// 서버 미연결 시 폴백으로 사용하며, 서버 성공 시에도 로컬 캐시로 동기화합니다.
/// 저장 경로: %LocalAppData%/UPM/whitelist.json
/// </summary>
public class LocalWhitelistStore
{
    private static readonly string _directory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UPM");

    private static readonly string _filePath = Path.Combine(_directory, "whitelist.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>로컬 파일에서 화이트리스트를 로드합니다. 파일이 없으면 빈 목록을 반환합니다.</summary>
    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new List<string>();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 로컬 화이트리스트 로드 실패: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>화이트리스트 전체를 로컬 파일에 저장합니다.</summary>
    public static void Save(List<string> items)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var toSave = items ?? new List<string>();
            // UPM 자체는 항상 보호 대상에 포함
            if (!toSave.Contains("UPM.Desktop", StringComparer.OrdinalIgnoreCase))
                toSave = new List<string>(toSave) { "UPM.Desktop" };
            var json = JsonSerializer.Serialize(toSave, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 로컬 화이트리스트 저장 실패: {ex.Message}");
        }
    }

    /// <summary>프로세스 이름을 로컬 화이트리스트에 추가합니다 (중복 무시).</summary>
    public static List<string> Add(string processName)
    {
        var list = Load();
        if (!string.IsNullOrWhiteSpace(processName) &&
            !list.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(processName);
            Save(list);
        }
        return list;
    }

    /// <summary>프로세스 이름을 로컬 화이트리스트에서 제거합니다.</summary>
    public static List<string> Remove(string processName)
    {
        var list = Load();
        list.RemoveAll(x => x.Equals(processName, StringComparison.OrdinalIgnoreCase));
        Save(list);
        return list;
    }
}
