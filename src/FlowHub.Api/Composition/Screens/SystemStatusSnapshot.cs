namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る運転状態の様子。変わるたびに番号を進め、画面は古い番号の様子を捨てる（届く順が入れ替わっても崩れないため）。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
/// <param name="State">運転状態。<c>"started"</c>（スタート）か <c>"stopped"</c>（停止）。HTTP の <c>GET /system/status</c> と同じ。</param>
internal sealed record SystemStatusSnapshot(long Version, string State);
