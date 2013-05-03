using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Ec2Manager.Support
{
    public static class HyperlinkUtilities
    {
        public static readonly DependencyProperty ActionProperty =
            DependencyProperty.RegisterAttached("Action", typeof(string), typeof(HyperlinkUtilities), new PropertyMetadata(null, ActionChanged));

        private static void ActionChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Hyperlink hyperlink = sender as Hyperlink;
            if (hyperlink == null)
                throw new InvalidOperationException("The attached Action property can only be applied to Hyperlink instances.");

            if (e.OldValue == null)
            {
                hyperlink.Click += (ho, he) =>
                    Caliburn.Micro.Action.Invoke(hyperlink.DataContext, e.NewValue as string);
            }
        }

        public static string GetAction(Hyperlink hyperlink)
        {
            if (hyperlink == null)
            {
                throw new ArgumentNullException("hyperlink");
            }

            return (string)hyperlink.GetValue(ActionProperty);
        }

        public static void SetAction(Hyperlink hyperlink, string Action)
        {
            if (hyperlink == null)
            {
                throw new ArgumentNullException("hyperlink");
            }

            hyperlink.SetValue(ActionProperty, Action);
        }
    }
}
