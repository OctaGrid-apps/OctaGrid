namespace OctaGrid.Connectors.BambuLab;

/// <summary>A command for the printer's request topic.</summary>
/// <param name="Name">The <c>command</c> field, which the printer repeats in its reply.</param>
/// <param name="SequenceId">The <c>sequence_id</c> field, which the printer repeats in its reply.</param>
/// <param name="Json">The message to publish.</param>
internal sealed record BambuCommand(string Name, string SequenceId, string Json);
