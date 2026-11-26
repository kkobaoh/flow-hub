namespace FlowHub.Identity.Domain;

internal sealed record UserId
{
    // 既存画面の String.trim() と同じ空白を取り除く。
    private const string TrimCharacters = "\t\n\v\f\r \u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000\ufeff";

    public UserId(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Value = input.AsSpan().Trim(TrimCharacters).ToString();
        if (Value.Length == 0 || Value == "setup")
        {
            throw new ArgumentException("空の ID と初期設定専用の setup はユーザーに登録できません。", nameof(input));
        }
    }

    public string Value { get; }
}
