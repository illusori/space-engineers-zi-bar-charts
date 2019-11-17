string _script_name = "Zephyr Industries Bar Charts";
string _script_version = "1.0.0";

string _script_title = null;
string _script_title_nl = null;

const int HISTORY     = 100;
const int SAMPLES     = 10;

const int PANELS_DEBUG = 0;
const int PANELS_WARN  = 1;
const int PANELS_CHART = 2;
const int SIZE_PANELS  = 3;

const string CHART_TIME = "Chart Exec Time";
const string CHART_LOAD = "Chart Instr Load";

List<string> _panel_tags = new List<string>(SIZE_PANELS) { "@DebugChartDisplay", "@WarningChartDisplay", "@ChartDisplay" };

/* Genuine global state */
int _cycles = 0;

List<List<IMyTextPanel>> _panels = new List<List<IMyTextPanel>>(SIZE_PANELS);
List<string> _panel_text = new List<string>(SIZE_PANELS) { "", "", "", "", "" };

double time_total = 0.0;

/* Reused single-run state objects, only global to avoid realloc/gc-thrashing */
// FIXME: _chart here? _panel?
List<string> _arguments = new List<string>();

public Program() {
    _script_title = $"{_script_name} v{_script_version}";
    _script_title_nl = $"{_script_name} v{_script_version}\n";

    Chart.Program = this;

    for (int i = 0; i < SIZE_PANELS; i++) {
        _panels.Add(new List<IMyTextPanel>());
    }

    // Create load/time charts
    Chart.Create(CHART_TIME, "us");
    Chart.Create(CHART_LOAD, "%");

    FindPanels();

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
        if ((updateSource & UpdateType.Update100) != 0) {
	    //DateTime start_time = DateTime.Now;
            // FIXME: System.Diagnostics.Stopwatch
            // Runtime.LastRunTimeMs
            // Runtime.TimeSinceLastRun

	    _cycles++;

	    Chart.Find(CHART_TIME).AddDatapoint(TimeAsUsec(Runtime.LastRunTimeMs));
            if (_cycles > 1) {
                time_total += Runtime.LastRunTimeMs;
                if (_cycles == 201) {
                    Warning($"Total time after 200 cycles: {time_total}ms.");
                }
            }

            ClearPanels(PANELS_DEBUG);

            Log(_script_title_nl);

            if ((_cycles % 30) == 0) {
                FindPanels();
            }

            Chart.DrawCharts();

	    Chart.Find(CHART_LOAD).AddDatapoint((double)Runtime.CurrentInstructionCount * 100.0 / (double)Runtime.MaxInstructionCount);

            // FIXME: grab from Dataset.Datapoints
	    long load_avg = (long)Chart.Find(CHART_LOAD).Avg;
	    long time_avg = (long)Chart.Find(CHART_TIME).Avg;
	    Log($"Load avg {load_avg}% in {time_avg}us");

            // Start at T-1 - exec time hasn't been updated yet.
            for (int i = 1; i < 16; i++) {
                long load = (long)Chart.Find(CHART_LOAD).Datapoint(-i);
                long time = (long)Chart.Find(CHART_TIME).Datapoint(-i);
                Log($"  [T-{i,-2}] Load {load} in {time}us");
            }
            Log($"Charts: {Chart.Count}, DrawBuffers: {Chart.BufferCount}");
            FlushToPanels(PANELS_DEBUG);
        }
        //if ((updateSource & (UpdateType.Trigger | UpdateType.Terminal)) != 0) {
        if (argument != "") {
            //Log($"Running command '{argument}'.");
            _arguments = ParseQuotedArguments(argument);
            //foreach (string word in _arguments) {
            //    Warning($"Parsed out word '{word}'.");
            //}
            // TODO: should require source script name as argument?
            if (_arguments.Count > 0) {
                if (_arguments[0] == "add") {
                    // add "chart name" value
                    if (_arguments.Count != 3) {
                        Warning("Syntax: add \"<chart name>\" <double value>");
                    } else {
                        Chart.Find(_arguments[1]).AddDatapoint(double.Parse(_arguments[2], System.Globalization.CultureInfo.InvariantCulture));
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
            } else {
                Warning($"Couldn't parse arguments in '{argument}'.");
            }
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

public double TimeAsUsec(double t) {
    //return (t * 1000.) / TimeSpan.TicksPerMillisecond;
    return t * 1000.0;
}

public void FindPanels() {
    for (int i = 0; i < SIZE_PANELS; i++) {
        _panels[i].Clear();
        GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(_panels[i], block => block.CustomName.Contains(_panel_tags[i]));
        for (int j = 0, szj = _panels[i].Count; j < szj; j++) {
            _panels[i][j].ContentType = ContentType.TEXT_AND_IMAGE;
            _panels[i][j].Font = "Monospace";
            _panels[i][j].FontSize = 0.5F;
            _panels[i][j].TextPadding = 0.5F;
            _panels[i][j].Alignment = TextAlignment.LEFT;
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

/*
IMyTextSurface textSurface = block.GetSurface(i);
frame = textSurface.DrawFrame();
sprite = MySprite.CreateText(string text, string fontId, Color color, float fontSize, TextAlignment textAlignment);
sprite.Position = new Vector2(textSurface.TextPadding, textSurface.TextPadding);
frame.Add(sprite);
 */
public void FlushToPanels(int kind) {
    for (int i = 0, sz = _panels[kind].Count; i < sz; i++) {
        if (_panels[kind][i] != null) {
            _panels[kind][i].WriteText(_panel_text[kind], false);
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

public class DrawBuffer {
    public IMyTextPanel Panel;
    public int X, Y;
    public List<StringBuilder> Buffer;
    public int ConfigHash;
    private List<string> template;
    string blank;

    public bool IsDirty { get; set; }

    public DrawBuffer(IMyTextPanel panel, int x, int y) {
        Panel = panel;
        X = x;
        Y = y;
        blank = new String(' ', X) + "\n";
        Buffer = new List<StringBuilder>(Y);
        template = new List<string>(Y);
        for (int i = 0; i < Y; i++) {
            Buffer.Add(new StringBuilder(blank));
            template.Add(blank);
        }
        ConfigHash = 0;
        IsDirty = true;
    }

    public void Save() {
        for (int i = 0; i < Y; i++) {
            template[i] = Buffer[i].ToString();
        }
    }

    public void Reset() {
        for (int i = 0; i < Y; i++) {
            Buffer[i].Clear().Append(template[i]);
        }
    }

    public void Clear() {
        for (int i = 0; i < Y; i++) {
            Buffer[i].Clear().Append(blank);
        }
    }

    public void Write(int x, int y, string s) {
        if (s.Length == 1) {
            Buffer[y][x] = s[0];
        } else {
            Buffer[y].Remove(x, s.Length).Insert(x, s);
        }
    }

    public void Flush() {
        if (Panel != null) {
            Panel.WriteText(ToString(), false);
        }
        IsDirty = false;
    }

    override public string ToString() {
        return string.Concat(Buffer);
    }
}

public class ViewPort {
    public DrawBuffer Buffer;
    public int offsetX, offsetY;
    public int X, Y;

    public bool IsDirty {
        get { return Buffer.IsDirty; }
        set { Buffer.IsDirty = value; }
    }

    public ViewPort(DrawBuffer buffer, int offset_x, int offset_y, int x, int y) {
        Buffer = buffer;
        offsetX = offset_x;
        offsetY = offset_y;
        X = x;
        Y = y;
    }

    public void Save() {
        Buffer.Save();
    }

    public void Write(int x, int y, string s) {
        // Yeah, no bounds checking. Sue me.
        Buffer.Write(offsetX + x, offsetY + y, s);
    }
}

public class ChartOptions {
    public bool Horizontal, ShowTitle, ShowCur, ShowAvg, ShowMax, ShowScale;

    public ChartOptions(bool horizontal = true, bool show_title = true, bool show_cur = true, bool show_avg = true, bool show_max = false, bool show_scale = true) {
        Horizontal = horizontal;
        ShowTitle = show_title;
        ShowCur = show_cur;
        ShowAvg = show_avg;
        ShowMax = show_max;
        ShowScale = show_scale;
    }
}

public class ChartDisplay {
    public ViewPort Viewport;
    public ChartOptions Options;

    public int? CurOffset = 0, AvgOffset = 0, MaxOffset = 0, ScaleOffset = 0;
    public double? SampleCur;
    public double SampleTotal, SampleMax, Scale;
    public int NumSamples;

    public bool IsDirty {
        get { return Viewport.IsDirty; }
        set { Viewport.IsDirty = value; }
    }

    public ChartDisplay(ViewPort viewport, ChartOptions options) {
        Viewport = viewport;
        Options = options;
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
        set { foreach (ChartDisplay display in displays) display.IsDirty = value; }
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
        chart.Unit = unit;
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

    public void AddViewPort(ViewPort viewport, ChartOptions options) {
        string label;
        ChartDisplay display = new ChartDisplay(viewport, options);

        displays.Add(display);

	viewport.Write(0, 0, "." + new String('-', viewport.X - 2) + ".");
        if (options.ShowTitle) {
            label = $"[{Title}]";
            if (label.Length < viewport.X - 2) {
                viewport.Write((viewport.X - label.Length) / 2, 0, label);
            }
        }
	for (int i = 1; i < viewport.Y - 1; i++) {
	    viewport.Write(0, i, "|");
	    viewport.Write(viewport.X - 1, i, "|");
	}
	viewport.Write(0, viewport.Y - 1, "." + new String('-', viewport.X - 2) + ".");
        if (options.ShowCur || options.ShowAvg || options.ShowMax || options.ShowScale) {
            List<string> segments = new List<string>(2);
            int offset = 1;
            if (options.ShowCur) {
                label = $"cur:     {Unit}";
                segments.Add(label);
                display.CurOffset = offset + 4;
                offset += label.Length + 1;
            }
            if (options.ShowAvg) {
                label = $"avg:     {Unit}";
                segments.Add(label);
                display.AvgOffset = offset + 4;
                offset += label.Length + 1;
            }
            if (options.ShowMax) {
                label = $"max:     {Unit}";
                segments.Add(label);
                display.MaxOffset = offset + 4;
                offset += label.Length + 1;
            }
            if (options.ShowScale) {
                string dim = options.Horizontal ? "Y" : "X";
                label = $"{dim}:     {Unit}";
                segments.Add(label);
                display.ScaleOffset = offset + 2;
                offset += label.Length + 1;
            }
            label = "[" + string.Join(" ", segments) + "]";
            if (label.Length < viewport.X - 2) {
                offset = (viewport.X - label.Length) / 2;
                viewport.Write(offset, viewport.Y - 1, label);
                display.CurOffset += offset;
                display.AvgOffset += offset;
                display.MaxOffset += offset;
                display.ScaleOffset += offset;
            } else {
                display.CurOffset = null;
                display.AvgOffset = null;
                display.MaxOffset = null;
                display.ScaleOffset = null;
            }
        }
	viewport.Save();
    }

    public void AddBuffer(DrawBuffer buffer, int offset_x, int offset_y, int x, int y, ChartOptions options) {
        AddViewPort(new ViewPort(buffer, offset_x, offset_y, x, y), options);
    }

    public void AddBuffer(DrawBuffer buffer, int offset_x, int offset_y, int x, int y) {
        AddViewPort(new ViewPort(buffer, offset_x, offset_y, x, y), new ChartOptions());
    }

    public void RemoveDisplays() {
        displays.Clear();
    }

    public void RemoveDisplaysForBuffer(DrawBuffer buffer) {
        displays.RemoveAll(display => display.Viewport.Buffer == buffer);
    }

    private void DrawBarToDisplay(int d, int t, double val, double max) {
        ChartDisplay display = displays[d];
        ViewPort viewport = display.Viewport;
        ChartOptions options = display.Options;
	int x, y, size, dx, dy;
	List<string> blocks;

        if (options.Horizontal) {
	    x = viewport.X - 2 - t;
            if (x < 1)
                return;
            y = viewport.Y - 2;
	    size = viewport.Y - 2;
	    blocks = _y_blocks;
	    dx = 0;
            dy = -1;
        } else {
	    x = 1;
            y = viewport.Y - 2 - t;
            if (y < 1)
                return;
	    size = viewport.X - 2;
	    blocks = _x_blocks;
	    dx = 1;
            dy = 0;
        }

        if (!display.SampleCur.HasValue)
            display.SampleCur = val;
        display.SampleTotal += val;
        if (val > display.SampleMax)
            display.SampleMax = val;
        display.Scale = max;
        display.NumSamples++;

        //parent.Log($"DrawBarToVP t{t}, v{val}, m{max}\nx{x}, y{y}, s{size}, dx{dx}, dy{dy}");

	double scaled = val * size / max;
	int repeat = (int)scaled;
	int fraction = (int)((scaled * 8.0) % (double)size);

	fraction = fraction >= 4 ? 4 : 0; // Only half block is implemented in font.

        // FIXME: unroll x/y versions and i < repeat and i == repeat cases.
	for (int i = 0; i < size; i++) {
	    if (i < repeat) {
                viewport.Buffer.Buffer[viewport.offsetY + y][viewport.offsetX + x] = blocks[8][0];
		//viewport.Write(x, y, blocks[8]); // TODO: unroll buffer write?
	    } else if (i == repeat) {
                viewport.Buffer.Buffer[viewport.offsetY + y][viewport.offsetX + x] = blocks[fraction][0];
		//viewport.Write(x, y, blocks[fraction]); // TODO: unroll buffer write?
		break;
	    }
	    x += dx;
	    y += dy;
	}
    }

    public void DrawBar(int t, double val, double max) {
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            DrawBarToDisplay(d, t, val, max);
        }
    }

    public void StartDraw() {
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            displays[d].SampleCur   = null;
            displays[d].SampleTotal = 0.0;
            displays[d].SampleMax   = 0.0;
            displays[d].Scale       = 0.0;
            displays[d].NumSamples  = 1;
        }
    }

    public void EndDraw() {
        float avg;
        ChartDisplay display;
        ViewPort viewport;
        ChartOptions options;
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            display = displays[d];
            viewport = display.Viewport;
            options = display.Options;
            avg = (float)display.SampleTotal / (float)display.NumSamples;
            if (options.ShowCur && display.CurOffset.HasValue && display.SampleCur.HasValue) {
                viewport.Write((int)display.CurOffset, viewport.Y - 1, $"{display.SampleCur,5:G4}");
            }
            if (options.ShowAvg && display.AvgOffset.HasValue) {
                viewport.Write((int)display.AvgOffset, viewport.Y - 1, $"{avg,5:G4}");
            }
            if (options.ShowMax && display.MaxOffset.HasValue) {
                viewport.Write((int)display.MaxOffset, viewport.Y - 1, $"{display.SampleMax,5:G4}");
            }
            if (options.ShowScale && display.ScaleOffset.HasValue) {
                viewport.Write((int)display.ScaleOffset, viewport.Y - 1, $"{display.Scale,5:G4}");
            }
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
        ResetChartBuffers();
        // FIXME: Ideally should spread this over different updates. Messes with composition though.
        foreach (Chart chart in _charts.Values) {
            chart.DrawChart();
        }
        FlushChartBuffers();
    }

    static public void MinimalDrawCharts() {
        foreach (Chart chart in _charts.Values) {
            if (chart.IsDataDirty) {
                chart.IsDisplayDirty = true;
            }
        }
        ResetDirtyChartBuffers();
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

    static public void ResetChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
	    buffer.Reset();
	}
    }

    static public void FlushChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
	    buffer.Flush();
	}
    }

    static public void ResetDirtyChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
            if (buffer.IsDirty) {
    	        buffer.Reset();
            }
	}
    }

    static public void FlushDirtyChartBuffers() {
	foreach (DrawBuffer buffer in _chart_buffers.Values) {
            if (buffer.IsDirty) {
    	        buffer.Flush();
            }
	}
    }

    static public void UpdatePanels(List<IMyTextPanel> panels) {
	// Special logic for ChartPanels, need to set up buffers and read their config.
	HashSet<long> found_ids = new HashSet<long>(panels.Count);
	DrawBuffer buffer;

	MyIniParseResult parse_result;
	List<string> sections = new List<string>();
	Chart chart;
	int width, height, x, y;
	bool horizontal, show_title, show_cur, show_avg, show_max, show_scale;
	string name;
	for (int i = 0, sz = panels.Count; i < sz; i++) {
	    IMyTextPanel panel = panels[i];
	    long id = panel.EntityId;
	    found_ids.Add(id);
	    if (!_chart_buffers.TryGetValue(id, out buffer)) {
		// 42x28 seems about right for 1x1 panel at 0.6
		// 52x35 for 1x1 panel at 0.5 with 0.5% padding.
		// 1x1 panel is 512 wide, 2x1 presumeably 1024 wide.
		float scale = panel.SurfaceSize.X / 512F;
		buffer = new DrawBuffer(panel, (int)(52F * scale), 35);
		_chart_buffers.Add(id, buffer);
	    } else {
		if (panel.CustomData.GetHashCode() == buffer.ConfigHash) {
		    //Program.Warning($"Chart panel skipping unchanged config parse on panel '{panel.CustomName}'");
		    continue;
		}
		buffer.Clear();
		buffer.Save();
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
		width = _ini.Get(section, "width").ToInt32(buffer.X);
		height = _ini.Get(section, "height").ToInt32(buffer.Y);
		x = _ini.Get(section, "x").ToInt32(0);
		y = _ini.Get(section, "y").ToInt32(0);
		// horizontal, etc ChartOptions settings.
		horizontal = _ini.Get(section, "horizontal").ToBoolean(true);
		show_title = _ini.Get(section, "show_title").ToBoolean(true);
		show_cur = _ini.Get(section, "show_cut").ToBoolean(true);
		show_avg = _ini.Get(section, "show_avg").ToBoolean(true);
		show_max = _ini.Get(section, "show_max").ToBoolean(false);
		show_scale = _ini.Get(section, "show_scale").ToBoolean(true);

		// Hmm, removing it here means we can't have multiples of same chart on same panel
		// TODO: maybe keep track of those chart names we've removed already in the sections loop?
		chart.RemoveDisplaysForBuffer(buffer);
		chart.AddBuffer(buffer, x, y, width, height, new ChartOptions(horizontal, show_title, show_cur, show_avg, show_max, show_scale));
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

    private int SafeMod(int val, int mod) {
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
