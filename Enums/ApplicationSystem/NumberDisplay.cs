namespace AGC_Management.Enums.ApplicationSystem;

// How a Number question is presented. Text is the existing free-text input; Slider and Dropdown
// both require MinValue/MaxValue to be a real range (not the "0 means unlimited" convention the
// free-text mode uses).
public enum NumberDisplay
{
    Text,
    Slider,
    Dropdown
}
