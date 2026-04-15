using System;
using System.Collections.Generic;
using GTA;

internal sealed class InstructionalButtonsRenderer : IDisposable
{
    internal sealed class ButtonSpec
    {
        internal ButtonSpec(int iconId, string text)
        {
            IconId = iconId;
            Text = text;
        }

        internal int IconId { get; }
        internal string Text { get; }
    }

    private readonly Scaleform _scaleform;
    private string _signature;

    internal InstructionalButtonsRenderer()
    {
        _scaleform = Scaleform.RequestMovie("INSTRUCTIONAL_BUTTONS");
    }

    internal bool IsReady => _scaleform != null && _scaleform.IsLoaded;

    internal void Draw(IReadOnlyList<ButtonSpec> buttons)
    {
        if (!IsReady)
        {
            return;
        }

        string signature = BuildSignature(buttons);
        if (!string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            Rebuild(buttons);
            _signature = signature;
        }

        _scaleform.Render2D();
    }

    private void Rebuild(IReadOnlyList<ButtonSpec> buttons)
    {
        _scaleform.CallFunction("CLEAR_ALL");
        _scaleform.CallFunction("SET_BACKGROUND");
        _scaleform.CallFunction("SET_MAX_WIDTH", 1.0f);

        for (int i = 0; i < buttons.Count; i++)
        {
            ButtonSpec button = buttons[i];
            _scaleform.CallFunction("SET_DATA_SLOT", i, button.IconId, button.Text);
        }

        _scaleform.CallFunction("DRAW_INSTRUCTIONAL_BUTTONS", 0);
    }

    private static string BuildSignature(IReadOnlyList<ButtonSpec> buttons)
    {
        if (buttons == null || buttons.Count == 0)
        {
            return string.Empty;
        }

        var values = new string[buttons.Count];
        for (int i = 0; i < buttons.Count; i++)
        {
            values[i] = buttons[i].IconId.ToString() + ':' + buttons[i].Text;
        }

        return string.Join("|", values);
    }

    public void Dispose()
    {
        _scaleform?.Dispose();
    }
}
