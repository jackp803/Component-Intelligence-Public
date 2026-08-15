namespace ComponentIntelligence.Extraction;

public static class SpecificationDictionary
{
    public static string? Map(string? section, string label)
    {
        var normalizedSection = Normalize(section);
        var normalized = Normalize(label);

        if (ContainsAny(normalized, "operating voltage", "supply voltage", "voltage - supply", "voltage supply", "power supply voltage", "rated supply voltage", "工作電壓", "工作电压", "供電電壓", "供电电压")) return "power.operating_voltage";
        if (ContainsAny(normalized, "current consumption", "current draw", "電流損耗", "电流损耗", "電流消耗", "电流消耗")) return "power.current_consumption";
        if (ContainsAny(normalized, "max current consumption", "maximum current consumption")) return "power.maximum_current";
        if (ContainsAny(normalized, "power consumption", "功率消耗")) return "power.power_consumption";
        if (ContainsAny(normalized, "reverse polarity protection", "反極性保護", "反极性保护")) return "power.reverse_polarity_protection";
        if (ContainsAny(normalized, "short circuit protection", "short-circuit protection", "短路保護", "短路保护")) return "power.short_circuit_protection";
        if (ContainsAny(normalized, "protection class", "防護等級", "防护等级") && !ContainsAny(normalized, "ip", "nema")) return "power.protection_class";

        if (ContainsAny(normalized, "number of inputs and outputs", "total number of inputs and outputs", "輸入和輸出總數", "输入和输出总数")) return "io.summary";
        if (ContainsAny(normalized, "number of digital inputs", "digital inputs", "數字輸入數", "数字输入数")) return "io.digital_input_count";
        if (ContainsAny(normalized, "number of digital outputs", "digital outputs", "數字輸出數", "数字输出数", "數字輸出數量", "数字输出数量")) return "io.digital_output_count";
        if (ContainsAny(normalized, "number of analogue outputs", "number of analog outputs", "analogue outputs", "analog outputs", "類比輸出", "模拟输出")) return "io.analog_output_count";
        if (ContainsAny(normalized, "number of analogue inputs", "number of analog inputs", "analogue inputs", "analog inputs", "類比輸入", "模拟输入")) return "io.analog_input_count";
        if (ContainsAny(normalized, "output function", "輸出功能", "输出功能")) return "io.output_function";
        if (normalized is "output type" or "輸出類型" or "输出类型") return "io.output_type";
        if (ContainsAny(normalized, "input type", "輸入類型", "输入类型")) return "io.input_type";
        if (ContainsAny(normalized, "electrical design", "電氣設計", "电气设计") && (normalizedSection.Contains("output") || string.IsNullOrEmpty(normalizedSection))) return "io.output_type";
        if (ContainsAny(normalized, "maximum current load per output", "max current load per output", "每個輸出最大電流負載", "每个输出最大电流负载")) return "io.output_max_current";

        if (normalized == "interface" || normalized is "介面" or "接口") return "communication.interface";
        if (ContainsAny(normalized, "communication interface", "通訊介面", "通讯接口", "通信介面", "通信接口")) return "communication.interface";
        if (normalized == "protocol" || normalized.EndsWith(" protocol", StringComparison.Ordinal) || normalized is "協定" or "协议") return "communication.protocol";
        if (ContainsAny(normalized, "transmission rate", "baud rate", "傳輸率", "传输率")) return "communication.baud_rate";
        if (ContainsAny(normalized, "transmission standard", "傳輸標準", "传输标准")) return "communication.standard";
        if (ContainsAny(normalized, "io-link revision", "io-link version")) return "communication.iolink_revision";
        if (ContainsAny(normalized, "port class a", "class a ports", "端口數量等級a", "端口数量等级a")) return "communication.iolink_class_a_ports";

        if (ContainsAny(normalized, "connector", "electrical connection plug", "electrical connection socket", "termination type", "connection type", "接插件", "接頭", "接头", "連接器", "连接器")) return "connector.raw";
        if (ContainsAny(normalized, "number of wires", "wire count", "芯線數", "芯线数", "線數", "线数")) return "wiring.conductor_count";
        if (ContainsAny(normalized, "process connection", "製程連接", "制程连接", "過程連接", "过程连接")) return "process.connection";

        if (ContainsAny(normalized, "measuring range", "measurement range", "量測範圍", "测量范围")) return "sensing.measuring_range";
        if (ContainsAny(normalized, "measuring element", "measurement element", "測量元件", "测量元件")) return "sensing.element";
        if (normalized is "sensor type" or "product type") return "classification.sensor_type";
        if (normalized == "series") return "classification.series";
        if (ContainsAny(normalized, "temperature monitoring", "溫度監控", "温度监控")) return "sensing.temperature_monitoring";
        if (ContainsAny(normalized, "type of pressure", "pressure type", "壓力類型", "压力类型")) return "sensing.pressure_type";
        if (ContainsAny(normalized, "vacuum resistance", "真空耐受", "真空耐受性")) return "sensing.vacuum_resistance";
        if (ContainsAny(normalized, "pressure rating", "耐壓", "耐压")) return "sensing.pressure_rating";
        if (ContainsAny(normalized, "burst pressure", "破裂壓力", "爆破壓力", "爆破压力")) return "sensing.burst_pressure";
        if (ContainsAny(normalized, "accuracy", "精度")) return "sensing.accuracy";
        if (ContainsAny(normalized, "repeatability", "重複精度", "重复精度")) return "sensing.repeatability";
        if (ContainsAny(normalized, "response time", "響應時間", "响应时间")) return "sensing.response_time";

        if (ContainsAny(normalized, "ambient temperature", "operating temperature", "環境溫度", "环境温度", "工作溫度", "工作温度")) return "environment.operating_temperature";
        if (ContainsAny(normalized, "storage temperature", "儲存溫度", "存储温度")) return "environment.storage_temperature";
        if (ContainsAny(normalized, "medium temperature", "media temperature", "介質溫度", "介质温度")) return "environment.medium_temperature";
        if (normalized is "protection" or "protection rating" or "ip rating" || ContainsAny(normalized, "degree of protection", "外殼防護等級", "外壳防护等级")) return "environment.ip_rating";
        if (ContainsAny(normalized, "nema")) return "environment.nema_rating";
        if (ContainsAny(normalized, "relative humidity", "相對濕度", "相对湿度")) return "environment.relative_humidity";
        if (ContainsAny(normalized, "media", "medium") && normalized is not "medium temperature") return "environment.media";

        if (ContainsAny(normalized, "dimensions", "尺寸")) return "mechanical.dimensions";
        if (normalized is "weight" || normalized.StartsWith("weight ", StringComparison.Ordinal) || normalized is "重量") return "mechanical.weight";
        if (ContainsAny(normalized, "housing", "package / case", "package / case (housing)", "外殼", "外壳")) return "mechanical.housing";
        if (ContainsAny(normalized, "mounting", "mounting type", "installation", "安裝方式", "安装方式")) return "mechanical.mounting";
        if (ContainsAny(normalized, "materials", "material", "probe material", "原材料", "材質", "材质")) return "mechanical.material";
        if (ContainsAny(normalized, "special feature", "特殊功能", "特殊特性")) return "general.special_feature";
        if (normalized is "application" or "應用" or "应用") return "general.application";
        if (ContainsAny(normalized, "no dead space", "無死角", "无死角")) return "process.no_dead_space";

        return null;
    }

    private static string Normalize(string? value) => string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static bool ContainsAny(string value, params string[] candidates) => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
