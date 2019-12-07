string _script_name = "Zephyr Industries Bar Charts";
string _script_version = "2.0.0";

string _script_title = null;
string _script_title_nl = null;

const string PUBSUB_SCRIPT_NAME = "Zephyr Industries PubSub Controller";
const string PUBSUB_ID = "zi.bar-charts";

const int HISTORY     = 100;
const int SAMPLES     = 10;

const int PANELS_DEBUG = 0;
const int PANELS_WARN  = 1;
const int PANELS_CHART = 2;
const int SIZE_PANELS  = 3;

const string CHART_EXEC_TIME = "Chart Exec Time";
const string CHART_MAIN_TIME = "Chart Main Time";
const string CHART_SUBS_TIME = "Chart Subs Time";
const string CHART_LOAD = "Chart Instr Load";
const string CHART_DATAPOINTS = "Chart Data In";

List<string> _panel_tags = new List<string>(SIZE_PANELS) { "@ChartDebugDisplay", "@ChartWarningDisplay", "@ChartDisplay" };

/* Genuine global state */
int _cycles = 0;

List<List<IMyTextSurfaceProvider>> _panels = new List<List<IMyTextSurfaceProvider>>(SIZE_PANELS);
List<string> _panel_text = new List<string>(SIZE_PANELS) { "", "", "", "", "" };
List<IMyProgrammableBlock> _pubsub_blocks = new List<IMyProgrammableBlock>();

double _time_total = 0.0;
double _last_run_time_ms_main_tally = 0.0;
double _last_run_time_ms_subs_tally = 0.0;
bool _last_run_main_loop = false;
int _last_run_datapoints = 0;

/* Reused single-run state objects, only global to avoid realloc/gc-thrashing */
// FIXME: _chart here? _panel?
List<string> _arguments = new List<string>();

public Program() {
    _script_title = $"{_script_name} v{_script_version}";
    _script_title_nl = $"{_script_name} v{_script_version}\n";

    Chart.Program = this;
    ChartDisplay.Program = this;
    DrawBuffer.Program = this;
    SlottedSprite.Program = this;

    for (int i = 0; i < SIZE_PANELS; i++) {
        _panels.Add(new List<IMyTextSurfaceProvider>());
    }

    // Create load/time charts
    Chart.Create(CHART_EXEC_TIME, "us");
    Chart.Create(CHART_MAIN_TIME, "us");
    Chart.Create(CHART_SUBS_TIME, "us");
    Chart.Create(CHART_LOAD, "%");
    Chart.Create(CHART_DATAPOINTS, "");

    FindPanels();
    FindPubSubBlocks();

    if (!Me.CustomName.Contains(_script_name)) {
        // Update our block to include our script name
        Me.CustomName = $"{Me.CustomName} - {_script_name}";
    }
    Log(_script_title);

    Runtime.UpdateFrequency |= UpdateFrequency.Update100;
}

public void Save() {
}

public void Main(string argument, UpdateType updateSource) {
    try {
        // Tally up all invocation times and record them as one on the non-command runs.
        if (_last_run_main_loop) {
            _last_run_time_ms_main_tally += Runtime.LastRunTimeMs;
            _last_run_main_loop = false;
        } else {
            _last_run_time_ms_subs_tally += Runtime.LastRunTimeMs;
        }
        if ((updateSource & UpdateType.Update100) != 0) {
	    //DateTime start_time = DateTime.Now;
            // FIXME: System.Diagnostics.Stopwatch
            // Runtime.LastRunTimeMs
            // Runtime.TimeSinceLastRun

	    _cycles++;

	    Chart.Find(CHART_EXEC_TIME).AddDatapoint(TimeAsUsec(_last_run_time_ms_main_tally + _last_run_time_ms_subs_tally));
	    Chart.Find(CHART_MAIN_TIME).AddDatapoint(TimeAsUsec(_last_run_time_ms_main_tally));
	    Chart.Find(CHART_SUBS_TIME).AddDatapoint(TimeAsUsec(_last_run_time_ms_subs_tally));
	    Chart.Find(CHART_DATAPOINTS).AddDatapoint((double)_last_run_datapoints);
            if (_cycles > 1) {
                _time_total += _last_run_time_ms_main_tally + _last_run_time_ms_subs_tally;
                if (_cycles == 201) {
                    Warning($"Total time after 200 cycles: {_time_total}ms.");
                }
            }
            _last_run_time_ms_main_tally = 0.0;
            _last_run_time_ms_subs_tally = 0.0;
            _last_run_datapoints = 0;
            _last_run_main_loop = true;

            ClearPanels(PANELS_DEBUG);

            Log(_script_title_nl);

            if ((_cycles % 30) == 0) {
                FindPanels();
            }
            if ((_cycles % 30) == 1) {
                FindPubSubBlocks();
            }

            Chart.DrawCharts();

	    Chart.Find(CHART_LOAD).AddDatapoint((double)Runtime.CurrentInstructionCount * 100.0 / (double)Runtime.MaxInstructionCount);

	    long load_avg = (long)Chart.Find(CHART_LOAD).Avg;
	    long time_avg = (long)Chart.Find(CHART_EXEC_TIME).Avg;
	    long time_subs_avg = (long)Chart.Find(CHART_SUBS_TIME).Avg;
	    long datapoints_tot = (long)Chart.Find(CHART_DATAPOINTS).Sum;
	    Log($"[Cycle {_cycles}]\n  [Avg ] Load {load_avg}% in {time_avg}us (Rx: {time_subs_avg}us/{datapoints_tot} points)");

            for (int i = 0; i < 16; i++) {
                long load = (long)Chart.Find(CHART_LOAD).Datapoint(-i);
                long time = (long)Chart.Find(CHART_EXEC_TIME).Datapoint(-i);
                long time_subs = (long)Chart.Find(CHART_SUBS_TIME).Datapoint(-i);
                long count_datapoints = (long)Chart.Find(CHART_DATAPOINTS).Datapoint(-i);
                Log($"  [T-{i,-2}] Load {load}% in {time}us (Rx: {time_subs}us/{count_datapoints} points)");
            }
            Log($"Charts: {Chart.Count}, DrawBuffers: {Chart.BufferCount}");
            FlushToPanels(PANELS_DEBUG);
        }
        //if ((updateSource & (UpdateType.Trigger | UpdateType.Terminal | UpdateType.Script)) != 0) {
        if (argument != "") {
            ProcessCommand(argument);
        }
    } catch (Exception e) {
        string mess = $"An exception occurred during script execution.\nException: {e}\n---";
        Log(mess);
        Warning(mess);
        FlushToPanels(PANELS_DEBUG);
        throw;
    }
}

// Super basic and noddy.
public List<string> ParseQuotedArguments(string argument) {
    List<string> words = new List<string>(argument.Split(' '));
    List<string> arguments = new List<string>();
    int? open = null;
    string word;

    if (!argument.Contains("\"")) {
        return words;
    }

    for (int i = 0; i < words.Count; i++) {
        //Warning($"{i} Parsing word '{words[i]}'");
        if (open == null) {
            //Warning($"Outside quotes");
            if (words[i][0] == '"') {
                //Warning($"Starts with quote");
                if (words[i][words[i].Length - 1] != '"') {
                    open = i;
                    continue;
                }
                //Warning($"...Also ends with quote");
                arguments.Add(words[i].Substring(1, words[i].Length - 2));
            } else {
                //Warning($"Adding word");
                arguments.Add(words[i]);
            }
        } else {
            //Warning($"Inside quotes from {open}");
            if (words[i][words[i].Length - 1] == '"') {
                //Warning($"Ends with quote");
                word = String.Join(" ", words.GetRange((int)open, i - (int)open + 1).ToArray());
                word = word.Substring(1, word.Length - 2);
                //Warning($"  word is {word}");
                arguments.Add(word);
                open = null;
            }
        }
    }
    return arguments;
}

public void ProcessCommand(string argument) {
    //Log($"Running command '{argument}'.");
    _arguments = ParseQuotedArguments(argument);
    //foreach (string word in _arguments) {
    //    Warning($"Parsed out word '{word}'.");
    //}
    // TODO: should require source script name as argument?
    if (_arguments.Count == 0) {
	Warning($"Couldn't parse arguments in '{argument}'.");
        return;
    }
    if (_arguments[0] == "event") {
        // FIXME: validation
	if (_arguments[2] == "datapoint.issue") {
	    Chart.Find(_arguments[3]).AddDatapoint(double.Parse(_arguments[4], System.Globalization.CultureInfo.InvariantCulture));
	    _last_run_datapoints++;
	} else if (_arguments[2] == "dataset.create") {
	    Chart.Create(_arguments[3], _arguments[4]);
	}
    } else if (_arguments[0] == "add") {
	// add "chart name" value
	if (_arguments.Count != 3) {
	    Warning("Syntax: add \"<chart name>\" <double value>");
	} else {
	    Chart.Find(_arguments[1]).AddDatapoint(double.Parse(_arguments[2], System.Globalization.CultureInfo.InvariantCulture));
	    _last_run_datapoints++;
	}
    } else if (_arguments[0] == "create") {
	// create "chart name" "units"
	if (_arguments.Count != 3) {
	    Warning("Syntax: create \"<chart name>\" \"<units>\"");
	} else {
	    Chart.Create(_arguments[1], _arguments[2]);
	}
    } else {
	Warning($"Unknown command '{_arguments[0]}'.");
    }
}

public double TimeAsUsec(double t) {
    //return (t * 1000.) / TimeSpan.TicksPerMillisecond;
    return t * 1000.0;
}

public void FindPubSubBlocks() {
    _pubsub_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(_pubsub_blocks, block => block.CustomName.Contains(PUBSUB_SCRIPT_NAME));
    IssueEvent("pubsub.register", $"datapoint.issue {Me.EntityId}");
    IssueEvent("pubsub.register", $"dataset.create {Me.EntityId}");
}

public void IssueEvent(string event_name, string event_args) {
    foreach (IMyProgrammableBlock block in _pubsub_blocks) {
        if (block != null) {
            block.TryRun($"event {PUBSUB_ID} {event_name} {event_args}");
        }
    }
}

public void FindPanels() {
    for (int i = 0; i < SIZE_PANELS; i++) {
        _panels[i].Clear();
        GridTerminalSystem.GetBlocksOfType<IMyTextSurfaceProvider>(_panels[i], block => ((IMyTerminalBlock)block).CustomName.Contains(_panel_tags[i]));
        if (i == PANELS_CHART)
            continue;
        for (int j = 0, szj = _panels[i].Count; j < szj; j++) {
            IMyTextPanel panel = _panels[i][j] as IMyTextPanel;
            panel.ContentType = ContentType.TEXT_AND_IMAGE;
            panel.Font = "Monospace";
            panel.FontSize = 0.5F;
            panel.TextPadding = 0.5F;
            panel.Alignment = TextAlignment.LEFT;
        }
    }
    Chart.UpdatePanels(_panels[PANELS_CHART]);
}

public void ClearAllPanels() {
    for (int i = 0; i < SIZE_PANELS; i++) {
        ClearPanels(i);
    }
}

public void ClearPanels(int kind) {
    _panel_text[kind] = "";
}

// FIXME: use System.Text.StringBuilder?
// StringBuilder.Clear() or StringBuilder.Length = 0
// new StringBuilder("", capacity);
// StringBuilder.Append(s)
// StringBuilder.ToString()
public void WritePanels(int kind, string s) {
    _panel_text[kind] += s;
}

public void PrependPanels(int kind, string s) {
    _panel_text[kind] = s + _panel_text[kind];
}

public void FlushToAllPanels() {
    for (int i = 0; i < SIZE_PANELS; i++) {
        FlushToPanels(i);
    }
}

public void FlushToPanels(int kind) {
    for (int i = 0, sz = _panels[kind].Count; i < sz; i++) {
        if (_panels[kind][i] != null) {
            ((IMyTextPanel)_panels[kind][i]).WriteText(_panel_text[kind], false);
        }
    }
}

public void Log(string s) {
    WritePanels(PANELS_DEBUG, s + "\n");
    Echo(s);
}

public void Warning(string s) {
    // Never clear buffer and and always immediately flush.
    // Prepend because long text will have the bottom hidden.
    PrependPanels(PANELS_WARN, $"[{DateTime.Now,11:HH:mm:ss.ff}] {s}\n");
    FlushToPanels(PANELS_WARN);
}

/*
IMyTextSurfaceProvider c = blocks[0] as IMyCockpit;
for (int x = 0; x < c.SurfaceCount; x++)
IMyTextSurface surface = block.GetSurface(i);
frame = surface.DrawFrame(); // Does a clear

Text:
sprite = MySprite.CreateText(string text, string fontId, Color color, float fontSize, TextAlignment textAlignment);
sprite = MySprite.CreateText("Stuff", "Debug", new Color(1f), 2f, TextAlignment.CENTER);
sprite.Position = new Vector2(surface.TextPadding, surface.TextPadding);
frame.Add(sprite);

Sprites:

surface.ContentType = ContentType.SCRIPT;
MySprite bar = new MySprite(SpriteType.TEXTURE, "SquareSimple", size: new Vector2(50f, 150f), color: new Color(1f));
bar.Position = surface.TextureSize / 2f - new Vector2(50f, 150f) / 2f;
frame.Add(bar);
 */
/* TODO:
 *  build list of sprites to draw?
 *  - knowing which have been queued already is issue?
 *  - can sprites be resized?
 *  - reserve slots in queue?
 *    - overwrite slot when new sprite issued?
 *    - save/reset eliminated: just static sprites in queue that never get overwritten.
 */
public class DrawBuffer {
    static public Program Program { set; get; }

    public IMyTextSurface Surface;
    private List<MySprite?> Buffer;
    public int ConfigHash;
    public Vector2 Size, Offset;

    public bool IsDirty { get; private set; }

    // FIXME: attach to surface not panel?
    public DrawBuffer(IMyTextSurface surface) {
        surface.ContentType = ContentType.SCRIPT;
        Offset = (surface.TextureSize - surface.SurfaceSize) / 2f;
        Size = surface.SurfaceSize * 1f;
        Buffer = new List<MySprite?>();
        Surface = surface;
        ConfigHash = 0;
        IsDirty = true;
    }

    // FIXME: explicit layers?
    // FIXME: slot class with slot index property?
    public List<int> Reserve(int reserve) {
        List<int> reserved = new List<int>(reserve);
        int slot = Buffer.Count();

        // TODO: should allow release of old slots and reuse? order matters though.
        for (int i = 0; i < reserve; i++) {
            Buffer.Add(null);
            reserved.Add(slot + i);
        }

        return reserved;
    }

    public void Write(int slot, MySprite sprite) {
        //DrawBuffer.Program.Log($"Write {slot}.");
        Buffer[slot] = sprite;
        IsDirty = true;
    }

    public void Flush() {
        if (Surface != null) {
            using (var frame = Surface.DrawFrame()) {
                foreach (MySprite? sprite in Buffer) {
                    if (sprite != null) {
                        //DrawBuffer.Program.Log($"Frame add p{sprite.Value.Position} s{sprite.Value.Size}.");
                        frame.Add(sprite.Value);
                    }
                }
            }
        }
        IsDirty = false;
    }
}

public class Viewport {
    public DrawBuffer Buffer;
    public Vector2 Offset = new Vector2(), Size = new Vector2();

    public bool IsDirty {
        get { return Buffer.IsDirty; }
        //set { Buffer.IsDirty = value; }
    }

    public Viewport(DrawBuffer buffer, Vector2 offset, Vector2 size) {
        Buffer = buffer;
        Offset = Buffer.Offset + offset;
        Size = size;
    }

    public Viewport SubViewport(Vector2 offset, Vector2 size) {
        return new Viewport(Buffer, Offset - Buffer.Offset + offset, size);
    }

    public List<int> Reserve(int reserve) {
        return Buffer.Reserve(reserve);
    }

    public void Write(int slot, MySprite sprite) {
        // TODO: Yeah, no bounds checking. Sue me.
        Buffer.Write(slot, sprite);
    }
}

public class ChartOptions {
    public bool Horizontal, ShowTitle, ShowCur, ShowAvg, ShowMax, ShowScale;
    public string Title, Unit;
    public int NumBars;
    public Color FgColor, BgColor, GoodColor, BadColor;
    public float WarnAbove, WarnBelow;

    public ChartOptions(bool horizontal = true, bool show_title = true, bool show_cur = true, bool show_avg = true, bool show_max = false, bool show_scale = true, string title = "", string unit = "", int num_bars = 30, float warn_above = Single.NaN, float warn_below = Single.NaN) {
        Horizontal = horizontal;
        ShowTitle = show_title;
        ShowCur = show_cur;
        ShowAvg = show_avg;
        ShowMax = show_max;
        ShowScale = show_scale;
        Title = title;
        Unit = unit;
        NumBars = num_bars;
        FgColor = new Color(0.8f);
        BgColor = new Color(0f);
        GoodColor = new Color(0f, 0.8f, 0f);
        BadColor = new Color(0.8f, 0f, 0f);
        WarnAbove = warn_above;
        WarnBelow = warn_below;
    }
}

public class SlottedSprite {
    static public Program Program { set; get; }

    public Viewport Viewport { get; private set; }
    public int Slot { get; private set; }

    public Vector2? Position { get; set; }
    public Vector2? Size { get; set; }
    public Color Color { get; set; }

    private MySprite? _sprite;
    public MySprite? Sprite {
        get { return _sprite; }
        set {
            _sprite = value;
            if (_sprite != null) {
                Size = _sprite?.Size;
                Color = (Color)_sprite?.Color;
            }
        }
    }

    public SlottedSprite(Viewport viewport, MySprite? sprite) {
        Viewport = viewport;
        Slot = Viewport.Reserve(1)[0];
        Sprite = sprite;
        if (Sprite != null) {
            Size = Sprite?.Size;
            Position = Sprite?.Position;
        } else {
            Size = new Vector2(0f);
            Position = new Vector2(0f);
        }
        Color = Sprite?.Color ?? new Color(1f);
    }

    public void Write() {
        if (Sprite != null) {
            // TODO: Yeah, no bounds checking. Sue me.
            // FIXME: is this going to work?
            MySprite sprite = Sprite.Value;
            sprite.Position = Viewport.Offset + Position;
            sprite.Size = Size;
            sprite.Color = Color;
            //SlottedSprite.Program.Log($"SlottedSprite.Write p{sprite.Position} s{sprite.Size} O{Viewport.Offset} P{Position}");
            Viewport.Write(Slot, sprite);
        }
    }
}

public class ChartDisplay {
    static public Program Program { set; get; }

    public Viewport FrameViewport;
    public Viewport ChartViewport;
    public ChartOptions Options;

    public int? CurOffset = 0, AvgOffset = 0, MaxOffset = 0, ScaleOffset = 0;
    public double? SampleCur;
    public double SampleTotal, SampleMax, Scale;
    public int NumSamples;

    private List<SlottedSprite> Bars, Frame;

    const int FRAME_BG            = 0;
    const int FRAME_BORDER        = 1;
    const int FRAME_INNER_BG      = 2;
    const int FRAME_TITLE_BORDER  = 3;
    const int FRAME_TITLE_BG      = 4;
    const int FRAME_TITLE         = 5;
    const int FRAME_STATUS_BORDER = 6;
    const int FRAME_STATUS_BG     = 7;
    const int FRAME_STATUS        = 8;
    const int SIZE_FRAME          = 9;

    //const float FRAME_LABEL_SCALE = 0.8f;
    //const float FRAME_PADDING = 32f;
    //const string FRAME_FONT = "Debug";

    const float FRAME_LABEL_SCALE = 0.6f;
    const float FRAME_PADDING = 24f;
    const string FRAME_FONT = "Monospace";

    public bool IsDirty {
        get { return FrameViewport.IsDirty; } // FIXME: needed anymore?
        //set { Viewport.IsDirty = value; }
    }

    public ChartDisplay(Viewport viewport, ChartOptions options) {
        FrameViewport = viewport;
        Vector2 padding = new Vector2(FRAME_PADDING);
        ChartViewport = FrameViewport.SubViewport(padding + 2f, (FrameViewport.Size - padding * 2f) - 4f);
        Options = options;

        ConfigureViewport();
    }

    public void ConfigureViewport() {
        ConfigureFrameViewport();
        ConfigureChartViewport();
    }

    public void ConfigureChartViewport() {
        Bars = new List<SlottedSprite>(Options.NumBars);
        for (int i = 0; i < Options.NumBars; i++) {
            // FIXME: configure thickness and time axis position
            Bars.Add(new SlottedSprite(ChartViewport,
                new MySprite(SpriteType.TEXTURE, "SquareSimple",
                    size: new Vector2(50f),
                    color: Options.FgColor)));
        }
    }


    public void ConfigureFrameViewport() {
        string label;
        MySprite sprite;

        Frame = new List<SlottedSprite>(SIZE_FRAME);
        for (int i = 0; i < SIZE_FRAME; i++) {
            Frame.Add(new SlottedSprite(FrameViewport, null));
        }
        // Two square sprites so we get a consistent line thickness, rather than scaling SquareHollow
        Vector2 outer = new Vector2((float)(((int)FRAME_PADDING / 2) - 1));
        Vector2 outer_size = FrameViewport.Size - outer * 2f;
        Vector2 inner = new Vector2((float)(((int)FRAME_PADDING / 2) + 1));
        Vector2 inner_size = FrameViewport.Size - inner * 2f;

        //ChartDisplay.Program.Warning($"outer:{outer} size:{outer_size}\n    inner:{inner} size:{inner_size}");
        Frame[FRAME_BG].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
            size: FrameViewport.Size,
            color: new Color(0f, 0.35f, 0.6f));
        Frame[FRAME_BG].Position = FrameViewport.Size / 2f;
        //Frame[FRAME_BG].Size = FrameViewport.Size;
        Frame[FRAME_BORDER].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
            size: outer_size,
            color: Options.FgColor);
        Frame[FRAME_BORDER].Position = FrameViewport.Size / 2f;
        //Frame[FRAME_BORDER].Size = outer_size;
        Frame[FRAME_INNER_BG].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
            size: inner_size,
            color: Options.BgColor);
        Frame[FRAME_INNER_BG].Position = FrameViewport.Size / 2f;
        //Frame[FRAME_INNER_BG].Size = inner_size;

        if (Options.ShowTitle) {
            label = $"{Options.Title}";
            sprite = MySprite.CreateText(label, FRAME_FONT, Options.FgColor, FRAME_LABEL_SCALE, TextAlignment.CENTER);
            sprite.Size = FrameViewport.Buffer.Surface.MeasureStringInPixels(new StringBuilder(label), FRAME_FONT, FRAME_LABEL_SCALE);
            //SlottedSprite.Program.Warning($"  setting size {sprite.Size} ({label})");
            if (sprite.Size?.X < inner_size.X) {
                Vector2 border = new Vector2((float)((int)sprite.Size?.X + 11), FRAME_PADDING - 2f);
                //SlottedSprite.Program.Warning($"  Title size fits");
                Frame[FRAME_TITLE].Sprite = sprite;
                Frame[FRAME_TITLE].Position = new Vector2(FrameViewport.Size.X / 2f, (FRAME_PADDING - (float)sprite.Size?.Y) / 2f);
                Frame[FRAME_TITLE_BG].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
                    size: border - 2f,
                    color: Options.BgColor);
                Frame[FRAME_TITLE_BG].Position = new Vector2(FrameViewport.Size.X / 2f, FRAME_PADDING / 2f);
                //Frame[FRAME_TITLE_BG].Size = Frame[FRAME_TITLE_BG].Sprite?.Size;
                Frame[FRAME_TITLE_BORDER].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
                    size: border,
                    color: Options.FgColor);
                Frame[FRAME_TITLE_BORDER].Position = new Vector2(FrameViewport.Size.X / 2f, FRAME_PADDING / 2f);
                //Frame[FRAME_TITLE_BORDER].Size = Frame[FRAME_TITLE_BORDER].Sprite?.Size;
            }
        }
    }

    public void StartDraw() {
	SampleCur   = null;
	SampleTotal = 0.0;
	SampleMax   = 0.0;
	Scale       = 0.0;
	NumSamples  = 1;
    }

    public void EndDraw() {
        DrawFrame();
    }

    public void DrawFrame() {
        string label;

        if (Options.ShowCur || Options.ShowAvg || Options.ShowMax || Options.ShowScale) {
            List<string> segments = new List<string>(2);
            if (Options.ShowCur) {
                label = $"cur:{SampleCur,5:G4}{Options.Unit}";
                segments.Add(label);
            }
            if (Options.ShowAvg) {
        	float avg = (float)SampleTotal / (float)NumSamples;
                label = $"avg:{avg,5:G4}{Options.Unit}";
                segments.Add(label);
            }
            if (Options.ShowMax) {
                label = $"max:{SampleMax,5:G4}{Options.Unit}";
                segments.Add(label);
            }
            if (Options.ShowScale) {
                string dim = Options.Horizontal ? "Y" : "X";
                label = $"{dim}:{Scale,6:G5}{Options.Unit}";
                segments.Add(label);
            }
            label = string.Join(" ", segments);
            // FIXME: this should be saved only built once in configure
            Vector2 inner = new Vector2((float)(((int)FRAME_PADDING / 2) + 1));
            Vector2 inner_size = FrameViewport.Size - inner * 2f;
            MySprite sprite = MySprite.CreateText(label, FRAME_FONT, Options.FgColor, FRAME_LABEL_SCALE, TextAlignment.CENTER);
            sprite.Size = FrameViewport.Buffer.Surface.MeasureStringInPixels(new StringBuilder(label), FRAME_FONT, FRAME_LABEL_SCALE);
            //SlottedSprite.Program.Warning($"  setting status size {sprite.Size} ({label})");
            if (sprite.Size?.X < inner_size.X) {
                Vector2 border = new Vector2((float)((int)sprite.Size?.X + 11), FRAME_PADDING - 2f);
                Frame[FRAME_STATUS].Sprite = sprite;
                Frame[FRAME_STATUS].Position = new Vector2(FrameViewport.Size.X / 2f, FrameViewport.Size.Y - FRAME_PADDING + (FRAME_PADDING - (float)sprite.Size?.Y) / 2f);
                Frame[FRAME_STATUS_BG].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
                    size: border - 2f,
                    color: Options.BgColor);
                Frame[FRAME_STATUS_BG].Position = new Vector2(FrameViewport.Size.X / 2f, FrameViewport.Size.Y - FRAME_PADDING / 2f);
                //Frame[FRAME_STATUS_BG].Size = Frame[FRAME_STATUS_BG].Sprite?.Size;
                Frame[FRAME_STATUS_BORDER].Sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
                    size: border,
                    color: Options.FgColor);
                Frame[FRAME_STATUS_BORDER].Position = new Vector2(FrameViewport.Size.X / 2f, FrameViewport.Size.Y - FRAME_PADDING / 2f);
                //Frame[FRAME_STATUS_BORDER].Size = Frame[FRAME_STATUS_BORDER].Sprite?.Size;
            }
        }

        // FIXME: frame needs drawing before bars, status after bars to get values.
        // FIXME: can be finagled by order of reservations.
        for (int i = 0; i < SIZE_FRAME; i++) {
            //ChartDisplay.Program.Log($"Writing frame {i}");
            Frame[i].Write();
        }
    }

    public void DrawBars() {
        // FIXME: move in here and access dataset ourselves.
    }

    public void DrawBar(int t, double val, double max) {
        //ChartDisplay.Program.Log($"DrawBarToVP top");

        int slot = Bars.Count - t - 1;

        if (slot < 0)
            return;

        // FIXME: Hmm, this is awkward if we aren't redrawing all bars.
        if (!SampleCur.HasValue)
            SampleCur = val;
        SampleTotal += val;
        if (val > SampleMax)
            SampleMax = val;
        Scale = max;
        NumSamples++;

        SlottedSprite slotted_sprite = Bars[slot];

        Vector2 breadth_mask, length_mask;

        if (Options.Horizontal) {
            breadth_mask = new Vector2(1f, 0f);
            length_mask = new Vector2(0f, 1f);
        } else {
            breadth_mask = new Vector2(0f, 1f);
            length_mask = new Vector2(1f, 0f);
        }
        if (Single.IsNaN(Options.WarnAbove) && Single.IsNaN(Options.WarnBelow)) {
            slotted_sprite.Color = Options.FgColor;
        } else {
	    if (!Single.IsNaN(Options.WarnAbove) && (val > (double)Options.WarnAbove)) {
                slotted_sprite.Color = Options.BadColor;
            } else if (!Single.IsNaN(Options.WarnBelow) && (val < (double)Options.WarnBelow)) {
                slotted_sprite.Color = Options.BadColor;
            } else {
                slotted_sprite.Color = Options.GoodColor;
	    }
        }

        //ChartDisplay.Program.Log($"DrawBarToVP t{t}, v{val}, m{max}");
        slotted_sprite.Size = (length_mask * ChartViewport.Size * (float)val / (float)max) +
            (breadth_mask * ((ChartViewport.Size / (float)Bars.Count) - 2f));
        slotted_sprite.Position = (breadth_mask * ((float)slot + 0.5f) * ChartViewport.Size / (float)Bars.Count) +
            (length_mask * new Vector2(-1f, 1f) * (new Vector2(0f, 1f) * ChartViewport.Size - slotted_sprite.Size / 2f));
        //ChartDisplay.Program.Log($"DrawBarToVP t{t}, v{val}, m{max}\n    s{slotted_sprite.Size} p{slotted_sprite.Position}");
        slotted_sprite.Write();
    }
}

public class Chart {
    static private List<string> _y_blocks = new List<string>(8) {
      " ", "\u2581", "\u2582", "\u2583", "\u2584", "\u2585", "\u2586", "\u2587", "\u2588",
    };
    static private List<string> _x_blocks = new List<string>(8) {
      " ", "\u258F", "\u258E", "\u258D", "\u258C", "\u258B", "\u258A", "\u2589", "\u2588",
    };

    static public Program Program { set; get; }

    // title => Chart
    static private Dictionary<string, Chart> _charts = new Dictionary<string, Chart>();
    static public int Count { get { return _charts.Count(); } }

    // panel.EntityId => DrawBuffer
    static private Dictionary<long, DrawBuffer> _chart_buffers = new Dictionary<long, DrawBuffer>();
    static public int BufferCount { get { return _chart_buffers.Count(); } }

    private List<ChartDisplay> displays;
    private Dataset dataset;

    public string Title { get; private set; }
    public string Unit { get; private set; }

    public bool IsViewed { get { return displays.Count > 0; } }
    public double Max { get { return dataset.Max; } }
    public double Sum { get { return dataset.Sum; } }
    public double Avg { get { return dataset.Avg; } }
    public int Length { get { return dataset.Length; } }
    public bool IsDataDirty { get { return dataset.IsDirty; } }
    public bool IsDisplayDirty {
        get { return displays.Any(display => display.IsDirty); }
        //set { foreach (ChartDisplay display in displays) display.IsDirty = value; }
    }

    /* Reused single-run state objects, only global to avoid realloc/gc-thrashing */
    static MyIni _ini = new MyIni();

    static public Chart Find(string title) {
        Chart chart;
        if (!_charts.TryGetValue(title, out chart)) {
            chart = new Chart(title);
            _charts.Add(title, chart);
        } else {
            // FIXME: probably should error or warn or something?
        }
        return chart;
    }

    static public Chart Create(string title, string unit) {
        Chart chart = Find(title);
        if (chart.Unit != unit) {
            // FIXME: make this default_unit and add default_title
            chart.Unit = unit;
            chart.ConfigureViewports();
        }
        return chart;
    }

    private Chart(string title) {
        Dataset.Program = Program; // h4x
        displays = new List<ChartDisplay>();
        Title = title;
        Unit = "";
        dataset = new Dataset();
    }

    public void AddDatapoint(double datapoint) {
        dataset.AddDatapoint(datapoint);
    }

    public double Datapoint(int offset) {
        return dataset.Datapoint(offset);
    }

    public void AddViewport(Viewport viewport, ChartOptions options) {
        ChartDisplay display = new ChartDisplay(viewport, options);

        displays.Add(display);
    }
    public void AddBuffer(DrawBuffer buffer, Vector2 offset, Vector2 size, ChartOptions options) {
        AddViewport(new Viewport(buffer, offset, size), options);
    }

    public void ConfigureViewports() {
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            displays[d].Options.Title = Title; // Ew. Codesmell.
            displays[d].Options.Unit = Unit;
            displays[d].ConfigureViewport();
        }
    }

    // FIXME: still needed?
    public void AddBuffer(DrawBuffer buffer, Vector2 offset, Vector2 size) {
        AddViewport(new Viewport(buffer, offset, size), new ChartOptions());
    }

    public void RemoveDisplays() {
        displays.Clear();
    }

    public void RemoveDisplaysForBuffer(DrawBuffer buffer) {
        displays.RemoveAll(display => display.FrameViewport.Buffer == buffer);
    }

    public void DrawBar(int t, double val, double max) {
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            displays[d].DrawBar(t, val, max);
        }
    }

    public void StartDraw() {
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            displays[d].StartDraw();
        }
    }

    public void EndDraw() {
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            displays[d].EndDraw();
        }
    }

    public void DrawChart() {
	//if (IsViewed && dataset.IsDirty) {
	if (IsViewed) {
	    double max = Max;
	    StartDraw();
	    for (int i = 0; i < HISTORY; i++) {
		//Log($"Update T-{i,-2}");
		DrawBar(i, dataset.Datapoint(-i), max);
	    }
	    EndDraw();
            dataset.Clean();
	}
    }

    static public void SimpleDrawCharts() {
        //ResetChartBuffers();
        // FIXME: Ideally should spread this over different updates. Messes with composition though.
        foreach (Chart chart in _charts.Values) {
            chart.DrawChart();
        }
        FlushChartBuffers();
    }

    static public void MinimalDrawCharts() {
        foreach (Chart chart in _charts.Values) {
            if (chart.IsDataDirty) {
                // FIXME: chart.IsDisplayDirty = true;
            }
        }
        //ResetDirtyChartBuffers();
        // FIXME: Ideally should spread this over different updates. Messes with composition though.
        foreach (Chart chart in _charts.Values) {
            if (chart.IsDisplayDirty) {
                chart.DrawChart();
            }
        }
        FlushDirtyChartBuffers();
    }

    static public void DrawCharts() {
        SimpleDrawCharts();
    }

    /* FIXME needed?
    static public void ResetChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
	    buffer.Reset();
	}
    }
     */

    static public void FlushChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
	    buffer.Flush();
	}
    }

    /* FIXME needed?
    static public void ResetDirtyChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
            if (buffer.IsDirty) {
    	        buffer.Reset();
            }
	}
    }
     */

    static public void FlushDirtyChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
            if (buffer.IsDirty) {
    	        buffer.Flush();
            }
	}
    }

    static public void UpdatePanels(List<IMyTextSurfaceProvider> panels) {
	// Special logic for ChartPanels, need to set up buffers and read their config.
	HashSet<long> found_ids = new HashSet<long>(panels.Count);
	DrawBuffer buffer;

	MyIniParseResult parse_result;
	List<string> sections = new List<string>();
	Chart chart;
	int width, height, x, y, num_bars;
	bool horizontal, show_title, show_cur, show_avg, show_max, show_scale;
        float warn_above, warn_below;
	string name, title, unit;
	for (int i = 0, sz = panels.Count; i < sz; i++) {
	    IMyTerminalBlock panel = (IMyTerminalBlock)panels[i];
	    long id = panel.EntityId;
	    found_ids.Add(id);
            //ChartDisplay.Program.Warning($"Configuring: \"{panel.CustomName}\"");
	    if (!_chart_buffers.TryGetValue(id, out buffer)) {
                // FIXME: multiple surface handling.
		buffer = new DrawBuffer(((IMyTextSurfaceProvider)panel).GetSurface(0));
                //ChartDisplay.Program.Warning($"New panel: \"{panel.Name}\"");
                //ChartDisplay.Program.Warning($"  panel: \"{panel.DefinitionDisplayNameText}\"");
                // FIXME: this may be localized???
                if (panel.DefinitionDisplayNameText.Contains("Corner LCD")) {
                    // Correct corner LCD surface sizes
                    if (panel.DefinitionDisplayNameText.Contains("Corner LCD Flat")) {
                        buffer.Size.Y = 88f;
                    } else {
                        buffer.Size.Y = 76f;
                    }
                    //ChartDisplay.Program.Warning($"fixing panel: \"{panel.CustomName}\", s{buffer.Size}");
                }
		_chart_buffers.Add(id, buffer);
	    } else {
		if (panel.CustomData.GetHashCode() == buffer.ConfigHash) {
		    //Program.Warning($"Chart panel skipping unchanged config parse on panel '{panel.CustomName}'");
		    continue;
		}
		//buffer.Clear();
		//buffer.Save();
	    }
	    buffer.ConfigHash = panel.CustomData.GetHashCode();
	    if (!_ini.TryParse(panel.CustomData, out parse_result)) {
		Program.Warning($"Chart panel parse error on panel '{panel.CustomName}' line {parse_result.LineNo}: {parse_result.Error}");
		found_ids.Remove(id); // Move along. Nothing to see. Pretend we never saw the panel.
		continue;
	    }
	    _ini.GetSections(sections);
	    foreach (string section in sections) {
		//Program.Warning($"Found section {section}");
		name = _ini.Get(section, "chart").ToString(section);
		chart = Find(name);
                /*
		if (!chart)) {
		    Program.Warning($"Chart panel '{panel.CustomName}' error in section '{section}': '{name}' is not the name of a known chart type."); // FIXME: list chart names
		    continue;
		}
                */
		width = _ini.Get(section, "width").ToInt32(100);
		height = _ini.Get(section, "height").ToInt32(100);
		x = _ini.Get(section, "x").ToInt32(0);
		y = _ini.Get(section, "y").ToInt32(0);
		// horizontal, etc ChartOptions settings.
		horizontal = _ini.Get(section, "horizontal").ToBoolean(true);
		show_title = _ini.Get(section, "show_title").ToBoolean(true);
		show_cur = _ini.Get(section, "show_cut").ToBoolean(true);
		show_avg = _ini.Get(section, "show_avg").ToBoolean(true);
		show_max = _ini.Get(section, "show_max").ToBoolean(false);
		show_scale = _ini.Get(section, "show_scale").ToBoolean(true);
                title = _ini.Get(section, "title").ToString(name);
                unit = _ini.Get(section, "unit").ToString(chart.Unit); // FIXME: prob gets overwritten by create commands
		num_bars = _ini.Get(section, "bars").ToInt32(30);
		warn_above = _ini.Get(section, "warn_above").ToSingle(Single.NaN);
		warn_below = _ini.Get(section, "warn_below").ToSingle(Single.NaN);

		// Hmm, removing it here means we can't have multiples of same chart on same panel
		// TODO: maybe keep track of those chart names we've removed already in the sections loop?
		chart.RemoveDisplaysForBuffer(buffer);
                // FIXME: clamp width/height, x/y
		chart.AddBuffer(buffer,
                    buffer.Size * new Vector2((float)x, (float)y) / 100f,
                    buffer.Size * new Vector2((float)width, (float)height) / 100f,
                    new ChartOptions(
                        horizontal: horizontal,
                        show_title: show_title,
                        show_cur: show_cur,
                        show_avg: show_avg,
                        show_max: show_max,
                        show_scale: show_scale,
                        title: title,
                        unit: unit,
                        num_bars: num_bars,
                        warn_above: warn_above,
                        warn_below: warn_below
                        ));
	    }
	}
	// Prune old ids in _chart_buffers
	HashSet<long> old_ids = new HashSet<long>(_chart_buffers.Keys);
	old_ids.ExceptWith(found_ids);
	foreach (long missing_id in old_ids) {
	    foreach (Chart mychart in _charts.Values) {
		mychart.RemoveDisplaysForBuffer(_chart_buffers[missing_id]);
	    }
	    _chart_buffers.Remove(missing_id);
	}
    }
}

class Dataset {
    static public Program Program { set; get; }

    private List<double> datapoints = new List<double>(HISTORY);

    public bool IsDirty { get; private set; }
    public int Count { get; private set; }
    public double Max { get { return datapoints.Max(); } }
    public double Sum { get { return datapoints.Sum(); } }
    public double Avg { get { return datapoints.Average(); } }
    public int Length { get { return datapoints.Count(); } }

    public Dataset() {
        Count = 0;
        //datapoints = new List<double>(HISTORY);
        for (int i = 0; i < HISTORY; i++) {
            datapoints.Add(0.0);
        }
        IsDirty = true;
    }

    public int SafeMod(int val, int mod) {
	while (val < 0)
	    val += mod;
	return val % mod;
    }
    private int Offset(int delta) { return SafeMod(Count + delta, HISTORY); }

    public double Datapoint(int offset) {
        return datapoints[Offset(offset)];
    }

    public void AddDatapoint(double datapoint) {
        Count++;
        datapoints[Offset(0)] = datapoint;
        IsDirty = true;
    }

    public void Clean() {
        IsDirty = false;
    }
}
