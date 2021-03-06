// ===========================================================
// Copyright (c) 2014-2015, Enrico Da Ros/kendar.org
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
// * Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// ===========================================================


using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace CoroutinesLib.Shared.Utils
{
#if FALSE
	internal class PrivateReflectionDynamicObject : DynamicObject
	{

		private static IDictionary<Type, IDictionary<string, IProperty>> _propertiesOnType = new ConcurrentDictionary<Type, IDictionary<string, IProperty>>();

		// Simple abstraction to make field and property access consistent
		interface IProperty
		{
			string Name { get; }
			object GetValue(object obj, object[] index);
			void SetValue(object obj, object val, object[] index);
		}

		// IProperty implementation over a PropertyInfo
		class Property : IProperty
		{
			internal PropertyInfo PropertyInfo { get; set; }

			string IProperty.Name
			{
				get
				{
					return PropertyInfo.Name;
				}
			}

			object IProperty.GetValue(object obj, object[] index)
			{
				return PropertyInfo.GetValue(obj, index);
			}

			void IProperty.SetValue(object obj, object val, object[] index)
			{
				PropertyInfo.SetValue(obj, val, index);
			}
		}

		// IProperty implementation over a FieldInfo
		class Field : IProperty
		{
			internal FieldInfo FieldInfo { get; set; }

			string IProperty.Name
			{
				get
				{
					return FieldInfo.Name;
				}
			}


			object IProperty.GetValue(object obj, object[] index)
			{
				return FieldInfo.GetValue(obj);
			}

			void IProperty.SetValue(object obj, object val, object[] index)
			{
				FieldInfo.SetValue(obj, val);
			}
		}


		private object RealObject { get; set; }
		private const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		internal static object WrapObjectIfNeeded(object o, bool loose = false)
		{
			// Don't wrap primitive types, which don't have many interesting internal APIs
			if (o == null || o.GetType().IsPrimitive || o is string)
				return o;

			return new PrivateReflectionDynamicObject() { RealObject = o, Loose = loose };
		}

		public override bool TryGetMember(GetMemberBinder binder, out object result)
		{
			IProperty prop = GetProperty(binder.Name);

			// Get the property value
			result = prop.GetValue(RealObject, index: null);

			// Wrap the sub object if necessary. This allows nested anonymous objects to work.
			result = WrapObjectIfNeeded(result);

			return true;
		}

		public override bool TrySetMember(SetMemberBinder binder, object value)
		{
			IProperty prop = GetProperty(binder.Name);

			// Set the property value
			prop.SetValue(RealObject, value, index: null);

			return true;
		}

		public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
		{
			// The indexed property is always named "Item" in C#
			IProperty prop = GetIndexProperty();
			result = prop.GetValue(RealObject, indexes);

			// Wrap the sub object if necessary. This allows nested anonymous objects to work.
			result = WrapObjectIfNeeded(result);

			return true;
		}

		public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object value)
		{
			// The indexed property is always named "Item" in C#
			IProperty prop = GetIndexProperty();
			prop.SetValue(RealObject, value, indexes);
			return true;
		}

		// Called when a method is called
		public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
		{
			result = InvokeMemberOnType(RealObject.GetType(), RealObject, binder.Name, args, Loose);

			// Wrap the sub object if necessary. This allows nested anonymous objects to work.
			result = WrapObjectIfNeeded(result);

			return true;
		}

		public override bool TryConvert(ConvertBinder binder, out object result)
		{
			result = Convert.ChangeType(RealObject, binder.Type);
			return true;
		}

		public override string ToString()
		{
			return RealObject.ToString();
		}

		private IProperty GetIndexProperty()
		{
			// The index property is always named "Item" in C#
			return GetProperty("Item");
		}

		private IProperty GetProperty(string propertyName)
		{

			// Get the list of properties and fields for this type
			IDictionary<string, IProperty> typeProperties = GetTypeProperties(RealObject.GetType());

			// Look for the one we want
			IProperty property;
			if (typeProperties.TryGetValue(propertyName, out property))
			{
				return property;
			}

			// The property doesn't exist

			// Get a list of supported properties and fields and show them as part of the exception message
			// For fields, skip the auto property backing fields (which name start with <)
			var propNames = typeProperties.Keys.Where(name => name[0] != '<').OrderBy(name => name);
			throw new ArgumentException(
					String.Format(
					"The property {0} doesn't exist on type {1}. Supported properties are: {2}",
					propertyName, RealObject.GetType(), String.Join(", ", propNames)));
		}

		private static IDictionary<string, IProperty> GetTypeProperties(Type type)
		{
			// First, check if we already have it cached
			IDictionary<string, IProperty> typeProperties;
			if (_propertiesOnType.TryGetValue(type, out typeProperties))
			{
				return typeProperties;
			}

			// Not cache, so we need to build it

			typeProperties = new ConcurrentDictionary<string, IProperty>();

			// First, add all the properties
			foreach (PropertyInfo prop in type.GetProperties(bindingFlags).Where(p => p.DeclaringType == type))
			{
				typeProperties[prop.Name] = new Property() { PropertyInfo = prop };
			}

			// Now, add all the fields
			foreach (FieldInfo field in type.GetFields(bindingFlags).Where(p => p.DeclaringType == type))
			{
				typeProperties[field.Name] = new Field() { FieldInfo = field };
			}

			// Finally, recurse on the base class to add its fields
			if (type.BaseType != null)
			{
				foreach (IProperty prop in GetTypeProperties(type.BaseType).Values)
				{
					typeProperties[prop.Name] = prop;
				}
			}

			// Cache it for next time
			_propertiesOnType[type] = typeProperties;

			return typeProperties;
		}

		private static object InvokeMemberOnType(Type type, object target, string name, object[] args, bool loose = false)
		{
			/*try
			{*/
			var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | bindingFlags)
				.FirstOrDefault(m =>
				{
					if (m.Name != name) return false;
					if (m.GetParameters().Length != args.Length) return false;
					for (int i = 0; i < args.Length; i++)
					{
						if (!m.GetParameters()[i].ParameterType.IsAssignableFrom(args[i].GetType())) return false;
					}
					return true;
				});

			if (method == null)
			{
				var excMsg = (args.Length == 1)
					? string.Format("A method named '{0}' to apply '{1}' is missing!", name, args[0].GetType().Name)
					: string.Format("A method named '{0}' is missing!", name);

				if (loose)
				{
					return null;
				}

				throw new NotImplementedException(excMsg);
			}

			return method.Invoke(target, args);
			/*return type.InvokeMember(
					name,
					BindingFlags.InvokeMethod | bindingFlags,
					null,
					target,
					args);
	}
	catch (MissingMethodException)
	{
		// If we couldn't find the method, try on the base class
		if (type.BaseType != null)
		{
			return InvokeMemberOnType(type.BaseType, target, name, args);
		}
		//quick greg hack to allow methods to not exist!
		return null;
	}*/
		}

		public bool Loose { get; set; }
	}


	public static class PrivateReflectionDynamicObjectExtensions
	{
		public static dynamic AsDynamic(this object value, bool loose = false)
		{
			return PrivateReflectionDynamicObject.WrapObjectIfNeeded(value, loose);
		}
	}

	public static class DelegateAdjuster
	{
		[SuppressMessage("Microsoft.Design", "CA1006:DoNotNestGenericTypesInMemberSignatures")]
		public static Action<TBase> CastArgument<TBase, TDerived>(Expression<Action<TDerived>> source) where TDerived : TBase
		{
			if (typeof(TDerived) == typeof(TBase))
			{
				return (Action<TBase>)((Delegate)source.Compile());

			}
			ParameterExpression sourceParameter = Expression.Parameter(typeof(TBase), "source");
			var result = Expression.Lambda<Action<TBase>>(
					Expression.Invoke(
							source,
							Expression.Convert(sourceParameter, typeof(TDerived))),
					sourceParameter);
			return result.Compile();
		}
	}
#endif
}

