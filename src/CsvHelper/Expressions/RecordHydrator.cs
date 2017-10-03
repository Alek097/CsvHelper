﻿// Copyright 2009-2017 Josh Close and Contributors
// This file is a part of CsvHelper and is dual licensed under MS-PL and Apache 2.0.
// See LICENSE.txt for details or visit http://www.opensource.org/licenses/ms-pl.html for MS-PL and http://opensource.org/licenses/Apache-2.0 for Apache 2.0.
// https://github.com/JoshClose/CsvHelper
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CsvHelper.Expressions
{
	/// <summary>
	/// Hydrates members of an existing record.
	/// </summary>
    public class RecordHydrator
    {
		private readonly CsvReader reader;
		private readonly ExpressionManager expressionManager;

		/// <summary>
		/// Creates a new instance using the given reader.
		/// </summary>
		/// <param name="reader">The reader.</param>
		public RecordHydrator( CsvReader reader )
		{
			this.reader = reader;
			expressionManager = new ExpressionManager( reader );
		}

		/// <summary>
		/// Hydrates members of the given record using the current reader row.
		/// </summary>
		/// <typeparam name="T">The record type.</typeparam>
		/// <param name="record">The record.</param>
        public void Hydrate<T>( T record )
		{
			try
			{
				GetHydrateRecordAction<T>()( record );
			}
			catch( TargetInvocationException ex )
			{
				throw ex.InnerException;
			}
		}

		/// <summary>
		/// Gets the action delegate used to hydrate a custom class object's members with data from the reader.
		/// </summary>
		/// <typeparam name="T">The record type.</typeparam>
		protected virtual Action<T> GetHydrateRecordAction<T>()
		{
			var recordType = typeof( T );

			if( !reader.context.HydrateRecordActions.TryGetValue( recordType, out Delegate action ) )
			{
				reader.context.HydrateRecordActions[recordType] = action = CreateHydrateRecordAction<T>();
			}

			return (Action<T>)action;
		}

		/// <summary>
		/// Creates the action delegate used to hydrate a record's members with data from the reader.
		/// </summary>
		/// <typeparam name="T">The record type.</typeparam>
		protected virtual Action<T> CreateHydrateRecordAction<T>()
		{
			var recordType = typeof( T );

			if( reader.context.ReaderConfiguration.Maps[recordType] == null )
			{
				reader.context.ReaderConfiguration.Maps.Add( reader.context.ReaderConfiguration.AutoMap( recordType ) );
			}

			var mapping = reader.context.ReaderConfiguration.Maps[recordType];

			var recordTypeParameter = Expression.Parameter( recordType, "record" );
			var memberAssignments = new List<Expression>();

			foreach( var memberMap in mapping.MemberMaps )
			{
				var fieldExpression = expressionManager.CreateGetFieldExpression( memberMap );
				if( fieldExpression == null )
				{
					continue;
				}

				var memberTypeParameter = Expression.Parameter( memberMap.Data.Member.MemberType(), "member" );
				var memberAccess = Expression.MakeMemberAccess( recordTypeParameter, memberMap.Data.Member );
				var memberAssignment = Expression.Assign( memberAccess, fieldExpression );
				memberAssignments.Add( memberAssignment );
			}

			foreach( var referenceMap in mapping.ReferenceMaps )
			{
				if( !reader.CanRead( referenceMap ) )
				{
					continue;
				}

				var referenceBindings = new List<MemberBinding>();
				expressionManager.CreateMemberBindingsForMapping( referenceMap.Data.Mapping, referenceMap.Data.Member.MemberType(), referenceBindings );

				Expression referenceBody;
				var constructorExpression = referenceMap.Data.Mapping.Constructor;
				if( constructorExpression is NewExpression )
				{
					referenceBody = Expression.MemberInit( (NewExpression)constructorExpression, referenceBindings );
				}
				else if( constructorExpression is MemberInitExpression )
				{
					var memberInitExpression = (MemberInitExpression)constructorExpression;
					var defaultBindings = memberInitExpression.Bindings.ToList();
					defaultBindings.AddRange( referenceBindings );
					referenceBody = Expression.MemberInit( memberInitExpression.NewExpression, defaultBindings );
				}
				else
				{
					// This is in case an IContractResolver is being used.
					var type = ReflectionHelper.CreateInstance( referenceMap.Data.Member.MemberType() ).GetType();
					referenceBody = Expression.MemberInit( Expression.New( type ), referenceBindings );
				}

				var memberTypeParameter = Expression.Parameter( referenceMap.Data.Member.MemberType(), "referenceMember" );
				var memberAccess = Expression.MakeMemberAccess( recordTypeParameter, referenceMap.Data.Member );
				var memberAssignment = Expression.Assign( memberAccess, referenceBody );
				memberAssignments.Add( memberAssignment );
			}

			var body = Expression.Block( memberAssignments );

			return Expression.Lambda<Action<T>>( body, recordTypeParameter ).Compile();
		}
	}
}