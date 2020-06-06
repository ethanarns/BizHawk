﻿using BizHawk.Emulation.Common;

// ReSharper disable StyleCop.SA1401
namespace BizHawk.Client.Common
{
	public static class Global
	{
		public static GameInfo Game { get; set; }
		public static IMovieSession MovieSession { get; set; }
		public static InputManager InputManager { get; } = new InputManager();
	}
}
