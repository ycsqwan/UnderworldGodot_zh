using Godot;
using Peaky.Coroutines;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace Underworld
{

    /// <summary>
    /// Class for processing message output being sent to the screen.
    /// </summary>
    public class MessageDisplay
    {
        /// <summary>
        /// DOS's seven message scroll escape colours, \0 to \6, as palette 0 indices.
        ///
        /// UW1 writes them as consecutive immediates through its font colour global in the
        /// scroll parser, UW.EXE 0x3976E through 0x397AA. UW2 looks them up in a table at
        /// UW2.EXE 0x69407, bytes 75 68 01 02 21 50 48.
        ///
        /// Three of these used to be hardcoded hex here, which is why they drifted: #883E14
        /// for \1 where DOS has (132,68,32) in UW1 and (124,64,36) in UW2, so it did not vary
        /// by game at all; white for \3 where DOS has (252,252,252); and 0x76 for \0 in UW2
        /// where the table says 0x75. Only the black for \2 was right.
        /// </summary>
        private static readonly byte[] ScrollColoursUw1 = { 0x2E, 0x26, 0xF1, 0x60, 0xB4, 0xC4, 0xD4 };
        private static readonly byte[] ScrollColoursUw2 = { 0x75, 0x68, 0x01, 0x02, 0x21, 0x50, 0x48 };

        /// <summary>The colour for one of DOS's scroll escapes, falling back to \0.</summary>
        public static string ScrollColour(int escape)
        {
            var table = UWClass._RES == UWClass.GAME_UW2 ? ScrollColoursUw2 : ScrollColoursUw1;
            if (escape < 0 || escape >= table.Length) { escape = 0; }
            return PaletteLoader.ToBBCode(0, table[escape]);
        }

        public enum MessageDisplayMode
        {
            NormalMode = 0,
            TypedInput = 1,
            TemporaryMessage = 2
        }
        /// <summary>
        /// Player has been prompted with a [MORE] wait
        /// </summary>
        public static bool WaitingForMore = false;

        /// <summary>
        /// Player has to type an input string
        /// </summary>
        public static bool WaitingForTypedInput = false;

        public static bool WaitingForYesOrNo = false;
        public static string YesNoOption = "Yes";

        public RichTextLabel[] OutputControl;

        public MessageScrollLine[] Lines = new MessageScrollLine[5];

        /// <summary>
        /// The number of ASCII characters including spaces to print on a single line.
        ///
        /// Still the authority on how wide a line may be, but as a pixel budget rather than a
        /// character count: the width of Columns capital A's is measured in the font the label
        /// actually uses, and that is what wrapping fills. Counting characters cannot work for
        /// Chinese, where a hanzi is about 1.3 of these cells, so a line of Columns hanzi would
        /// run a third wider than the box and be clipped. Kept as a character count so English
        /// lines wrap exactly where the DOS original wrapped them.
        /// </summary>
        public int Columns = 64;

        /// <summary>
        /// The number of rows of text to print.
        /// </summary>
        public int Rows = 5;

        public int LinePtr = 0;
        public void Clear()
        {
            for (int i = 0; i <= Lines.GetUpperBound(0); i++)
            {
                Lines[i].SetLine("");
            }
            LinePtr = 0;
            UpdateMessageDisplay();
        }

        private IEnumerator AddLineWithMore(string newText, int Option, int Colour = 0)
        {
            if (newText.Trim() == "") { yield return 0; }
            newText = $"[color={ScrollColour(Colour)}]{newText}[/color]";
            if (LinePtr <= Lines.GetUpperBound(0))
            {
                Lines[LinePtr++].SetLine(newText, Option);
            }
            else
            {
                //shift all lines up.
                for (int i = 1; i <= Lines.GetUpperBound(0); i++)
                {
                    Lines[i - 1].SetLine(Lines[i].LineText, Lines[i].OptionNo);
                    //TODO associate option no with a click event index.
                }
                Lines[Rows - 1].SetLine(newText, Option);
            }

            UpdateMessageDisplay();
            yield return 0;
        }


        public IEnumerator AddLine(string newText, int Option, int Colour = 0, bool ForceMore = false)
        {
            if (newText.Trim() != "")
            {
                if (ForceMore)
                {//causes [more] to happen without splitting.
                    yield return AddLineWithMore(newText: newText, Option: Option, Colour: ConversationVM.UI_SAY);
                    WaitingForMore = true;
                    while (WaitingForMore)
                    {
                        yield return new WaitOneFrame();
                    }
                    LinePtr--;//overwrite last line
                }
                else
                {
                    var newTextSplit = newText.Split("[MORE]");
                    for (int i = 0; i <= newTextSplit.GetUpperBound(0); i++)
                    {
                        if (i > 0)
                        {
                            yield return AddLineWithMore(newText: "[MORE]", Option: Option, Colour: ConversationVM.UI_SAY);
                            WaitingForMore = true;
                            while (WaitingForMore)
                            {
                                yield return new WaitOneFrame();
                            }
                            LinePtr--;//overwrite last line
                        }
                        yield return AddLineWithMore(newText: newTextSplit[i], Option: Option, Colour: Colour);
                    }
                    yield return 0;
                }
            }


        }

        /// <summary>
        /// Outputs all text to the control
        /// </summary>
        public void UpdateMessageDisplay()
        {
            //output all text 
            var output = "";
            for (int i = 0; i <= Lines.GetUpperBound(0); i++)
            {
                if (output.Length > 0)
                {
                    output += "\n";
                }
                output += Lines[i].LineText;
            }
            //Replace special characters
            if (MessageDisplay.WaitingForYesOrNo)
            {
                uimanager.instance.TypedInput.Text = MessageDisplay.YesNoOption;
                output = output.Replace("{TYPEDINPUT}", MessageDisplay.YesNoOption);
            }
            else
            {
                if (MessageDisplay.WaitingForTypedInput)
                {
                    output = output.Replace("{TYPEDINPUT}", uimanager.instance.TypedInput.Text);
                }
                else if (uimanager.SaveDescriptionPromptActive)
                {
                    // The save description prompt keeps its own state rather than the shared
                    // WaitingForTypedInput, because it has to tell an empty commit from a
                    // cancel and the two mean opposite things.
                    output = output.Replace("{TYPEDINPUT}", uimanager.SaveDescriptionText);
                }
            }

            OutputControl[0].Text = output;
        }

        public IEnumerator AddText(string newText, int option = -1, int colour = 0)
        {
            if (newText.EndsWith("\n"))
            {//trims an ending new line
                newText = newText.Substring(0, newText.Length - 1);
            }
            int NoOfRowsNeeded = 0;
            //split by new lines
            newText = newText.Replace("\\m", " [MORE] ");
            var TextLines = newText.Split('\n');
            foreach (var textline in TextLines)
            {
                foreach (string wrapped in WrapLine(textline))
                {
                    if (NoOfRowsNeeded >= Rows - 1)
                    {//To force a [more] to appear when text is longer than the control
                        yield return AddLine(newText: "[MORE]", Option: -1, Colour: ConversationVM.UI_SAY, ForceMore: true);
                        NoOfRowsNeeded = 0;
                    }
                    NoOfRowsNeeded++;
                    yield return AddLine(newText: wrapped, Option: option, Colour: colour);
                }
            }
            yield return 0;
        }

        /// <summary>
        /// Breaks one line of text into the lines that will fit the message scroll.
        ///
        /// Wrapping on measured width rather than on character count, because the character
        /// count cannot serve both scripts: at the message log's font size 64 an ASCII cell is
        /// 20px and a hanzi is 25.85px, so Columns hanzi would overrun the box by a third. The
        /// pixel budget is Columns ASCII cells wide, which keeps English wrapping exactly where
        /// the DOS original wrapped it and lets Chinese fill the same space.
        ///
        /// A hanzi is its own break opportunity, as it is in Chinese typesetting; a run of
        /// Latin, digits and punctuation is not, so words stay whole.
        /// </summary>
        private List<string> WrapLine(string textline)
        {
            if (!TryGetWrapMetrics(out Font font, out int fontSize, out float maxWidth))
            {
                // No label to measure against yet (AddText can run before the UI is up).
                return WrapByCharacterCount(textline);
            }

            var tokens = new List<string>();
            var spaceBefore = new List<bool>();
            var prefix = new StringBuilder();
            bool pendingSpace = false;

            int i = 0;
            while (i < textline.Length)
            {
                char c = textline[i];
                if (c == ' ')
                {
                    pendingSpace = true;
                    i++;
                    continue;
                }
                if (c == '[')
                {
                    // A bracketed run is markup, not text: it measures as nothing. It is held
                    // back and stuck onto the next token so that AddLine still sees [MORE] and
                    // the colour tags where it expects them.
                    int close = textline.IndexOf(']', i + 1);
                    if (close < 0)
                    {
                        prefix.Append(textline, i, textline.Length - i);
                        break;
                    }
                    prefix.Append(textline, i, close - i + 1);
                    i = close + 1;
                    continue;
                }
                if (IsCjk(c))
                {
                    tokens.Add(prefix.ToString() + c);
                    prefix.Clear();
                    spaceBefore.Add(pendingSpace);
                    pendingSpace = false;
                    i++;
                    continue;
                }
                int start = i;
                while (i < textline.Length && textline[i] != ' ' && textline[i] != '[' && !IsCjk(textline[i]))
                {
                    i++;
                }
                tokens.Add(prefix.ToString() + textline.Substring(start, i - start));
                prefix.Clear();
                spaceBefore.Add(pendingSpace);
                pendingSpace = false;
            }
            if (prefix.Length > 0)
            {
                tokens.Add(prefix.ToString());
                spaceBefore.Add(pendingSpace);
            }

            float spaceWidth = font.GetStringSize(" ", HorizontalAlignment.Left, -1, fontSize).X;
            var lines = new List<string>();
            var current = new StringBuilder();
            float width = 0f;

            for (int t = 0; t < tokens.Count; t++)
            {
                string token = tokens[t];
                float tokenWidth = MeasureVisible(font, fontSize, token);
                bool needSpace = spaceBefore[t] && current.Length > 0;
                float add = needSpace ? spaceWidth : 0f;

                if (current.Length > 0 && width + add + tokenWidth > maxWidth)
                {
                    lines.Add(current.ToString().TrimEnd());
                    current.Clear();
                    width = 0f;
                    needSpace = false;
                    add = 0f;
                }

                if (needSpace)
                {
                    current.Append(' ');
                }
                current.Append(token);
                width += add + tokenWidth;
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString().TrimEnd());
            }
            if (lines.Count == 0)
            {
                lines.Add("");
            }
            return lines;
        }

        /// <summary>
        /// The font, its size and the pixel width a line may fill, read off the label that will
        /// draw the text so that wrapping and rendering cannot disagree.
        /// </summary>
        private bool TryGetWrapMetrics(out Font font, out int fontSize, out float maxWidth)
        {
            font = null;
            fontSize = 0;
            maxWidth = 0f;
            if (OutputControl == null || OutputControl.Length == 0 || OutputControl[0] == null)
            {
                return false;
            }
            RichTextLabel control = OutputControl[0];
            font = control.GetThemeFont("normal_font");
            fontSize = control.GetThemeFontSize("normal_font_size");
            if (font == null || fontSize <= 0)
            {
                return false;
            }
            float box = control.Size.X - 2f;
            if (box <= 0f)
            {
                return false;
            }
            // Columns ASCII cells, or the box if the box is the tighter of the two.
            float budget = font.GetStringSize(new string('A', Mathf.Max(Columns, 1)),
                HorizontalAlignment.Left, -1, fontSize).X;
            maxWidth = budget > 0f ? Mathf.Min(box, budget) : box;
            return maxWidth > 0f;
        }

        private static float MeasureVisible(Font font, int fontSize, string text)
        {
            return font.GetStringSize(StripMarkup(text), HorizontalAlignment.Left, -1, fontSize).X;
        }

        private static string StripMarkup(string text)
        {
            if (text.IndexOf('[') < 0)
            {
                return text;
            }
            var plain = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '[')
                {
                    int close = text.IndexOf(']', i + 1);
                    if (close >= 0)
                    {
                        i = close + 1;
                        continue;
                    }
                }
                plain.Append(text[i]);
                i++;
            }
            return plain.ToString();
        }

        /// <summary>
        /// Whether a character is written one-per-cell and may be broken before or after,
        /// which is every CJK script plus its fullwidth punctuation.
        /// </summary>
        private static bool IsCjk(char c)
        {
            return (c >= '\u2E80' && c <= '\u9FFF')   // radicals, kana, unified ideographs
                || (c >= '\uAC00' && c <= '\uD7AF')   // hangul syllables
                || (c >= '\uF900' && c <= '\uFAFF')   // compatibility ideographs
                || (c >= '\uFE30' && c <= '\uFE4F')   // compatibility forms
                || (c >= '\uFF00' && c <= '\uFF60')   // fullwidth forms
                || (c >= '\uFFE0' && c <= '\uFFE6');
        }

        /// <summary>
        /// The original word-splitting wrapper, kept for the case where there is no label to
        /// measure against. Counts characters, so it is only right for English.
        /// </summary>
        private List<string> WrapByCharacterCount(string textline)
        {
            var lines = new List<string>();
            var Words = textline.Split(' ');
            bool firstWord = false;//Always add the first word.
            string LineToAdd = "";
            var length = 0;
            int WordPtr = 0;
            for (WordPtr = 0; WordPtr <= Words.GetUpperBound(0); WordPtr++)
            {
                bool AddNewLine = false;
                length = LineToAdd.Length;
                if (!firstWord)
                {
                    LineToAdd = Words[WordPtr] + " ";
                    firstWord = true;
                }
                else
                {
                    while (WordPtr <= Words.GetUpperBound(0) && AddNewLine == false)
                    {
                        AddNewLine = false;
                        if (length + Words[WordPtr].Length <= Columns)
                        {//space to add next word
                            LineToAdd += Words[WordPtr] + " ";
                            WordPtr++;
                        }
                        else
                        {
                            AddNewLine = true;
                            lines.Add(LineToAdd.TrimEnd());
                            LineToAdd = Words[WordPtr] + " "; //new word. new line.
                        }
                        length = LineToAdd.Length;
                    }//end while
                }
            }//end loop
            if (LineToAdd != "")
            {//Store remaining data
                lines.Add(LineToAdd.TrimEnd());
            }
            return lines;
        }

        public IEnumerator RestoreLinesAfterWait(MessageScrollLine[] linesToRestore, float waittime)
        {
            yield return new WaitForSeconds(waittime);
            //restore
            for (int i = 0; i <= Lines.GetUpperBound(0); i++)
            {
                Lines[i] = new MessageScrollLine(linesToRestore[i].OptionNo, linesToRestore[i].LineText);
            }
            UpdateMessageDisplay();
            uimanager.MessageScrollIsTemporary = false;
            yield return 0;
        }

    }//end class
}//end namespace
