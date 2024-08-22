using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace CraftHub.Converters
{
	public class TypeToDescriptionConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is Type type)
			{
				switch (type)
				{
					case Type t when t == typeof(int):
						return "32-bit signed integer. Range: -2,147,483,648 to 2,147,483,647.";
					case Type t when t == typeof(float):
						return "Single-precision 32-bit floating-point number. Range: approximately ±1.5 × 10^−45 to ±3.4 × 10^38.";
					case Type t when t == typeof(bool):
						return "Boolean value representing true or false.";
					case Type t when t == typeof(string):
						return "A sequence of characters representing text.";
					case Type t when t == typeof(double):
						return "Double-precision 64-bit floating-point number. Range: approximately ±5.0 × 10^−324 to ±1.7 × 10^308.";
					case Type t when t == typeof(decimal):
						return "128-bit decimal number with high precision. Used for financial and monetary calculations.";
					case Type t when t == typeof(byte):
						return "8-bit unsigned integer. Range: 0 to 255.";
					case Type t when t == typeof(short):
						return "16-bit signed integer. Range: -32,768 to 32,767.";
					case Type t when t == typeof(char):
						return "Single Unicode character, representing a character from text.";
					default:
						return "Unknown type";
				}
			}
			return "Unknown type";
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}

