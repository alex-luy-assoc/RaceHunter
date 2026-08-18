namespace RaceHunter.Gemini.Schemas;

public static class AgentSchemas
{
    public const string PlanV1 = """
        {"type":"object","additionalProperties":false,"required":["schemaVersion","actors","invariant","strategy"],"properties":{"schemaVersion":{"type":"string","enum":["plan-v1"]},"actors":{"type":"array","minItems":1,"items":{"type":"object","additionalProperties":false,"required":["name","operationId"],"properties":{"name":{"type":"string"},"operationId":{"type":"string"}}}},"invariant":{"type":"object","additionalProperties":false,"required":["type","metric"],"properties":{"type":{"type":"string"},"metric":{"type":"string"},"maximum":{"type":["number","null"]},"leftMetric":{"type":["string","null"]},"rightMetric":{"type":["string","null"]},"relation":{"type":["string","null"],"enum":["equal","less-than-or-equal","greater-than-or-equal",null]}}},"strategy":{"type":"object","additionalProperties":false,"required":["kind","actorCount","seed"],"properties":{"kind":{"type":"string"},"actorCount":{"type":"integer","minimum":1},"seed":{"type":"integer"}}}}}
        """;

    public const string StrategyV1 = """
        {"type":"object","additionalProperties":false,"required":["schemaVersion","action","actorCount","strategy","timingAdjustmentMs","rationaleSummary"],"properties":{"schemaVersion":{"type":"string","enum":["strategy-v1"]},"action":{"type":"string","enum":["change-actor-count","select-strategy","adjust-timing","repeat","start-minimization","stop"]},"actorCount":{"type":"integer","minimum":1},"strategy":{"type":"string"},"timingAdjustmentMs":{"type":"integer","minimum":0,"maximum":5000},"rationaleSummary":{"type":"string"}}}
        """;
}
