using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Cached input for reusing a dead pawn's prepared draw requests and matrices.
	/// </summary>
	public struct CorpseRenderState
	{
		public PawnDrawParms Parms;
		public int Seen;
		public bool HasPerRequestMaterial;
		public bool Valid;
	}
}