using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoomDesk.Core;

public sealed record IdentityCardData(string Name, string DocumentNumber);

// Keyboard-wedge devices deliver text. A vendor SDK adapter can return the same data later.
// This parses a transport format; it does not authenticate a card or verify its chip.
public static class IdentityCardInput
{
    public static IdentityCardData Parse(string? raw)
    {
        var text = raw?.Trim() ?? "";
        if (text.Length == 0) throw new BoardException("请先让设备输入或粘贴姓名和身份证号。");
        if (text.Length > 4096) throw new BoardException("读卡内容过长，请检查设备输出格式。");
        string name, number;
        if (text.StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(text);
                name = json.RootElement.GetProperty("name").GetString() ?? "";
                number = json.RootElement.GetProperty("documentNumber").GetString() ?? "";
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            { throw new BoardException("JSON 格式应包含 name 和 documentNumber 两个文本字段。"); }
        }
        else
        {
            // Strict, full input match prevents an address or another person's number being silently selected.
            var labelled = Regex.Match(text, @"\A姓名\s*[:：]\s*(?<name>[^\r\n\t,，;；:：]+?)[\s,，;；]+(?:公民身份号码|公民身份证号码|身份证号码|身份证号|证件号码)\s*[:：]\s*(?<id>[0-9]{17}[0-9Xx])\s*\z");
            var pair = labelled.Success ? labelled : Regex.Match(text, @"\A(?<name>[^\r\n\t,，;；:：]+?)[\s,，;；]+(?<id>[0-9]{17}[0-9Xx])\s*\z");
            if (!pair.Success) throw new BoardException("未识别完整姓名和 18 位身份证号。请使用“姓名 + Tab/换行 + 身份证号”，或带标签的两行文本。");
            name = pair.Groups["name"].Value; number = pair.Groups["id"].Value;
        }
        name = name.Trim(); number = number.Trim().ToUpperInvariant();
        if (name.Length is 0 or > 80 || name.Any(char.IsControl) || name.Any(char.IsDigit))
            throw new BoardException("姓名格式不正确，请核对设备输出。");
        if (!Regex.IsMatch(number, @"\A[0-9]{17}[0-9X]\z"))
            throw new BoardException("需要完整的 18 位身份证号，请核对设备输出。");
        return new IdentityCardData(name, number);
    }
}
