﻿using Avalonia;
using Avalonia.Markup.Xaml;

namespace VtNetCore.Avalonia.App
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
