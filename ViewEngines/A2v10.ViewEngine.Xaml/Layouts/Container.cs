﻿// Copyright © 2015-2023 Oleksandr Kukhtin. All rights reserved.

namespace A2v10.Xaml;

[ContentProperty("Children")]
public abstract class Container : UIElement
{
	public UIElementCollection Children { get; set; } = [];

	public Object? ItemsSource { get; set; }

	public virtual void RenderChildren(RenderContext context, Action<TagBuilder>? onRenderStatic = null)
	{
		var tml = new TagBuilder("template");
		onRenderStatic?.Invoke(tml);
		MergeAttributes(tml, context, MergeAttrMode.Visibility);
		var isBind = GetBinding(nameof(ItemsSource));
		if (isBind != null)
		{
			tml.MergeAttribute("v-for", $"(xelem, xIndex) in {isBind.GetPath(context)}");
			tml.RenderStart(context);
			using (new ScopeContext(context, "xelem", isBind.Path))
			{
				foreach (var c in Children)
				{
					c.RenderElement(context, (tag) =>
					{
						onRenderStatic?.Invoke(tag);
						tag.MergeAttribute(":key", "xIndex");
					});
				}
			}
			tml.RenderEnd(context);
		}
		else
		{
			tml.RenderStart(context);
			foreach (var c in Children)
			{
				c.RenderElement(context, onRenderStatic);
			}
			tml.RenderEnd(context);
		}
	}

	protected override void OnEndInit()
	{
		base.OnEndInit();
		foreach (var c in Children)
			c.SetParent(this);
	}

	public override void OnSetStyles(RootContainer root)
	{
		base.OnSetStyles(root);
		foreach (var c in Children)
			c.OnSetStyles(root);
	}
}
