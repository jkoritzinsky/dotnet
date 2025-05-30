// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

/// Primary relations on types and signatures, with the exception of
/// constraint solving and method overload resolution.
module internal FSharp.Compiler.SignatureConformance

open System.Text

open Internal.Utilities.Collections
open Internal.Utilities.Library
open Internal.Utilities.Library.Extras
open FSharp.Compiler
open FSharp.Compiler.DiagnosticsLogger
open FSharp.Compiler.Features
open FSharp.Compiler.InfoReader
open FSharp.Compiler.Syntax
open FSharp.Compiler.SyntaxTreeOps
open FSharp.Compiler.Text
open FSharp.Compiler.TypedTree
open FSharp.Compiler.TypedTreeBasics
open FSharp.Compiler.TypedTreeOps
open FSharp.Compiler.TypeHierarchy

#if !NO_TYPEPROVIDERS
open FSharp.Compiler.TypeProviders
#endif

type TypeMismatchSource = NullnessOnlyMismatch | RegularMismatch

exception RequiredButNotSpecified of DisplayEnv * ModuleOrNamespaceRef * string * (StringBuilder -> unit) * range

exception ValueNotContained of kind:TypeMismatchSource * DisplayEnv * InfoReader * ModuleOrNamespaceRef * Val * Val * (string * string * string -> string)

exception UnionCaseNotContained of DisplayEnv * InfoReader * Tycon * UnionCase * UnionCase * (string * string -> string)

exception FSharpExceptionNotContained of DisplayEnv * InfoReader * Tycon * Tycon * (string * string -> string)

exception FieldNotContained of kind:TypeMismatchSource * DisplayEnv * InfoReader * Tycon * Tycon * RecdField * RecdField * (string * string -> string)

exception InterfaceNotRevealed of DisplayEnv * TType * range

exception ArgumentsInSigAndImplMismatch of sigArg: Ident * implArg: Ident

exception DefinitionsInSigAndImplNotCompatibleAbbreviationsDiffer of
    denv: DisplayEnv *
    implTycon:Tycon *
    sigTycon:Tycon *
    implTypeAbbrev:TType *
    sigTypeAbbrev:TType *
    range: range

// Use a type to capture the constant, common parameters 
type Checker(g, amap, denv, remapInfo: SignatureRepackageInfo, checkingSig) = 

        // Build a remap that maps tcrefs in the signature to tcrefs in the implementation
        // Used when checking attributes.
        let sigToImplRemap = 
            let remap = Remap.Empty 
            let remap = (remapInfo.RepackagedEntities, remap) ||> List.foldBack (fun (implTcref, signTcref) acc -> addTyconRefRemap signTcref implTcref acc) 
            let remap = (remapInfo.RepackagedVals, remap) ||> List.foldBack (fun (implValRef, signValRef) acc -> addValRemap signValRef.Deref implValRef.Deref acc) 
            remap
            
        // For all attributable elements (types, modules, exceptions, record fields, unions, parameters, generic type parameters)
        //
        // (a)	Start with lists AImpl and ASig containing the attributes in the implementation and signature, in declaration order
        // (b)	Each attribute in AImpl is checked against the available attributes in ASig. 
        //     a.	If an attribute is found in ASig which is an exact match (after evaluating attribute arguments), then the attribute in the implementation is ignored, the attribute is removed from ASig, and checking continues
        //     b.	If an attribute is found in ASig that has the same attribute type, then a warning is given and the attribute in the implementation is ignored 
        //     c.	Otherwise, the attribute in the implementation is kept
        // (c)	The attributes appearing in the compiled element are the compiled forms of the attributes from the signature and the kept attributes from the implementation
        let checkAttribs _aenv (implAttribs: Attribs) (sigAttribs: Attribs) fixup =
            
            // Remap the signature attributes to make them look as if they were declared in 
            // the implementation. This allows us to compare them and propagate them to the implementation
            // if needed.
            let sigAttribs = sigAttribs |> List.map (remapAttrib g sigToImplRemap)

            // Helper to check for equality of evaluated attribute expressions
            let attribExprEq (AttribExpr(_, e1)) (AttribExpr(_, e2)) = EvaledAttribExprEquality g e1 e2

            // Helper to check for equality of evaluated named attribute arguments
            let attribNamedArgEq (AttribNamedArg(nm1, ty1, isProp1, e1)) (AttribNamedArg(nm2, ty2, isProp2, e2)) = 
                (nm1 = nm2) && 
                typeEquiv g ty1 ty2 && 
                (isProp1 = isProp2) && 
                attribExprEq e1 e2

            let attribsEq  attrib1 attrib2 = 
                let (Attrib(implTcref, _, implArgs, implNamedArgs, _, _, _implRange)) = attrib1
                let (Attrib(signTcref, _, signArgs, signNamedArgs, _, _, _signRange)) = attrib2
                tyconRefEq g signTcref implTcref &&
                (implArgs, signArgs) ||> List.lengthsEqAndForall2 attribExprEq &&
                (implNamedArgs, signNamedArgs) ||> List.lengthsEqAndForall2 attribNamedArgEq

            let attribsHaveSameTycon attrib1 attrib2 = 
                let (Attrib(implTcref, _, _, _, _, _, _)) = attrib1
                let (Attrib(signTcref, _, _, _, _, _, _)) = attrib2
                tyconRefEq g signTcref implTcref 

            // For each implementation attribute, only keep if it is not mentioned in the signature.
            // Emit a warning if it is mentioned in the signature and the arguments to the attributes are 
            // not identical.
            let rec check keptImplAttribsRev implAttribs sigAttribs = 
                match implAttribs with 
                | [] -> keptImplAttribsRev |> List.rev
                | implAttrib :: remainingImplAttribs -> 

                    // Look for an attribute in the signature that matches precisely. If so, remove it 
                    let lookForMatchingAttrib =  sigAttribs |> List.tryRemove (attribsEq implAttrib)
                    match lookForMatchingAttrib with 
                    | Some (_, remainingSigAttribs) -> check keptImplAttribsRev remainingImplAttribs remainingSigAttribs    
                    | None ->

                    // Look for an attribute in the signature that has the same type. If so, give a warning
                    let existsSimilarAttrib = sigAttribs |> List.exists (attribsHaveSameTycon implAttrib)

                    if existsSimilarAttrib then 
                        let (Attrib(implTcref, _, _, _, _, _, implRange)) = implAttrib
                        warning(Error(FSComp.SR.tcAttribArgsDiffer(implTcref.DisplayName), implRange))
                        check keptImplAttribsRev remainingImplAttribs sigAttribs    
                    else
                        check (implAttrib :: keptImplAttribsRev) remainingImplAttribs sigAttribs    
                
            let keptImplAttribs = check [] implAttribs sigAttribs 

            fixup (sigAttribs @ keptImplAttribs)
            true

        let rec checkTypars m (aenv: TypeEquivEnv) (implTypars: Typars) (sigTypars: Typars) = 
            if implTypars.Length <> sigTypars.Length then 
                errorR (Error(FSComp.SR.typrelSigImplNotCompatibleParamCountsDiffer(), m)) 
                false
            else 
              let aenv = aenv.BindEquivTypars implTypars sigTypars 
              (implTypars, sigTypars) ||> List.forall2 (fun implTypar sigTypar -> 
                  let m = sigTypar.Range
                  
                  let check =
                      if g.langVersion.SupportsFeature LanguageFeature.InterfacesWithAbstractStaticMembers then
                          implTypar.StaticReq = TyparStaticReq.HeadType && sigTypar.StaticReq = TyparStaticReq.None
                      else
                          implTypar.StaticReq <> sigTypar.StaticReq
                  if check then
                      errorR (Error(FSComp.SR.typrelSigImplNotCompatibleCompileTimeRequirementsDiffer(), m))
                
                  // Adjust the actual type parameter name to look like the signature
                  implTypar.SetIdent (mkSynId implTypar.Range sigTypar.Id.idText)     

                  // Mark it as "not compiler generated", now that we've got a good name for it
                  implTypar.SetCompilerGenerated false 

                  // Check the constraints in the implementation are present in the signature
                  implTypar.Constraints |> List.forall (fun implTyparCx -> 
                      match implTyparCx with 
                      // defaults can be dropped in the signature 
                      | TyparConstraint.DefaultsTo(_, _acty, _) -> true
                      | _ -> 
                          if not (List.exists  (typarConstraintsAEquiv g aenv implTyparCx) sigTypar.Constraints)
                          then (errorR(Error(FSComp.SR.typrelSigImplNotCompatibleConstraintsDiffer(sigTypar.Name, LayoutRender.showL(NicePrint.layoutTyparConstraint denv (implTypar, implTyparCx))), m)); false)
                          else  true) &&

                  // Check the constraints in the signature are present in the implementation
                  sigTypar.Constraints |> List.forall (fun sigTyparCx -> 
                      match sigTyparCx with 
                      // defaults can be present in the signature and not in the implementation  because they are erased
                      | TyparConstraint.DefaultsTo(_, _acty, _) -> true
                      // 'comparison' and 'equality' constraints can be present in the signature and not in the implementation  because they are erased
                      | TyparConstraint.SupportsComparison _ -> true
                      | TyparConstraint.SupportsEquality _ -> true
                      | _ -> 
                          if not (List.exists  (fun implTyparCx -> typarConstraintsAEquiv g aenv implTyparCx sigTyparCx) implTypar.Constraints) then
                              (errorR(Error(FSComp.SR.typrelSigImplNotCompatibleConstraintsDifferRemove(sigTypar.Name, LayoutRender.showL(NicePrint.layoutTyparConstraint denv (sigTypar, sigTyparCx))), m)); false)
                          else  
                              true) &&
                  (not checkingSig || checkAttribs aenv implTypar.Attribs sigTypar.Attribs (implTypar.SetAttribs)))

        and checkTypeDef (aenv: TypeEquivEnv) (infoReader: InfoReader) (implTycon: Tycon) (sigTycon: Tycon) =
            let m = implTycon.Range
            
            implTycon.SetOtherXmlDoc(sigTycon.XmlDoc)
            
            // Propagate defn location information from implementation to signature . 
            sigTycon.SetOtherRange (implTycon.Range, true)
            implTycon.SetOtherRange (sigTycon.Range, false)
            
            if implTycon.LogicalName <> sigTycon.LogicalName then 
                errorR (Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleNamesDiffer(implTycon.TypeOrMeasureKind.ToString(), sigTycon.LogicalName, implTycon.LogicalName), m))
                false 
            else
            
            if implTycon.CompiledName <> sigTycon.CompiledName then 
                errorR (Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleNamesDiffer(implTycon.TypeOrMeasureKind.ToString(), sigTycon.CompiledName, implTycon.CompiledName), m))
                false 
            else
            
            checkExnInfo  (fun f -> FSharpExceptionNotContained(denv, infoReader, implTycon, sigTycon, f)) aenv infoReader implTycon sigTycon implTycon.ExceptionInfo sigTycon.ExceptionInfo &&
            
            let implTypars = implTycon.Typars m
            let sigTypars = sigTycon.Typars m
            
            if implTypars.Length <> sigTypars.Length then  
                errorR (Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleParameterCountsDiffer(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)) 
                false
            elif isLessAccessible implTycon.Accessibility sigTycon.Accessibility then 
                errorR(Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleAccessibilityDiffer(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)) 
                false
            else 
                let aenv = aenv.BindEquivTypars implTypars sigTypars 

                let implIntfTys = implTycon.ImmediateInterfaceTypesOfFSharpTycon 
                let sigIntfTys = sigTycon.ImmediateInterfaceTypesOfFSharpTycon 
                let implUserIntfTys = implTycon.TypeContents.tcaug_interfaces |> List.filter (fun (_, compgen, _) -> not compgen) |> List.map p13 
                let flatten tys = 
                   tys 
                   |> List.collect (AllSuperTypesOfType g amap m AllowMultiIntfInstantiations.Yes) 
                   |> ListSet.setify (typeEquiv g) 
                   |> List.filter (isInterfaceTy g)
                let implIntfTys     = flatten implIntfTys 
                let sigIntfTys     = flatten sigIntfTys 
              
                let unimplIntfTys = ListSet.subtract (fun sigIntfTy implIntfTy -> typeAEquiv g aenv implIntfTy sigIntfTy) sigIntfTys implIntfTys
                (unimplIntfTys 
                 |> List.forall (fun ity -> 
                    let errorMessage = FSComp.SR.DefinitionsInSigAndImplNotCompatibleMissingInterface(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName, NicePrint.minimalStringOfType denv ity)
                    errorR (Error(errorMessage, m)); false)) &&
                    
                let implUserIntfTys = flatten implUserIntfTys

                let hidden = ListSet.subtract (typeAEquiv g aenv) implUserIntfTys sigIntfTys
                let continueChecks, warningOrError = if implTycon.IsFSharpInterfaceTycon then false, errorR else true, warning
                (hidden |> List.forall (fun ity -> warningOrError (InterfaceNotRevealed(denv, ity, implTycon.Range)); continueChecks)) &&

                let aNull = IsUnionTypeWithNullAsTrueValue g implTycon
                let fNull = IsUnionTypeWithNullAsTrueValue g sigTycon
                if aNull && not fNull then 
                    errorR(Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleImplementationSaysNull(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m))
                    false
                elif fNull && not aNull then 
                    errorR(Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleSignatureSaysNull(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m))
                    false
                else

                let aNull2 = TypeNullIsExtraValue g m (generalizedTyconRef g (mkLocalTyconRef implTycon))
                let fNull2 = TypeNullIsExtraValue g m (generalizedTyconRef g (mkLocalTyconRef implTycon)) // TODO: should be sigTycon, raises extra errors
                if aNull2 && not fNull2 then 
                    errorR(Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleImplementationSaysNull2(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m))
                    false
                elif fNull2 && not aNull2 then 
                    errorR(Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleSignatureSaysNull2(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m))
                    false
                else

                let aSealed = isSealedTy g (generalizedTyconRef g (mkLocalTyconRef implTycon))
                let fSealed = isSealedTy g (generalizedTyconRef g (mkLocalTyconRef sigTycon))
                if aSealed && not fSealed then 
                    errorR(Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleImplementationSealed(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m))
                    false
                elif not aSealed && fSealed then
                    errorR(Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleImplementationIsNotSealed(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m))
                    false
                else

                let aPartial = isAbstractTycon implTycon
                let fPartial = isAbstractTycon sigTycon
                if aPartial && not fPartial then 
                    errorR(Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleImplementationIsAbstract(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m))
                    false
                elif not aPartial && fPartial then 
                    errorR(Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleSignatureIsAbstract(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m))
                    false
                elif not (typeAEquiv g aenv (superOfTycon g implTycon) (superOfTycon g sigTycon)) then 
                    errorR (Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleTypesHaveDifferentBaseTypes(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m))
                    false
                else

                checkTypars m aenv implTypars sigTypars &&
                checkTypeRepr m aenv infoReader implTycon sigTycon &&
                checkTypeAbbrev m aenv implTycon sigTycon &&
                checkAttribs aenv implTycon.Attribs sigTycon.Attribs (fun attribs -> implTycon.entity_attribs <- attribs) &&
                checkModuleOrNamespaceContents implTycon.Range aenv infoReader (mkLocalEntityRef implTycon) sigTycon.ModuleOrNamespaceType
            
        and checkValInfo aenv err (implVal : Val) (sigVal : Val) = 
            let id = implVal.Id
            match implVal.ValReprInfo, sigVal.ValReprInfo with 
            | _, None -> true
            | None, Some _ -> err(FSComp.SR.ValueNotContainedMutabilityArityNotInferred)
            | Some (ValReprInfo (implTyparNames, implArgInfos, implRetInfo) as implValInfo), Some (ValReprInfo (sigTyparNames, sigArgInfos, sigRetInfo) as sigValInfo) ->
                let ntps = implTyparNames.Length
                let mtps = sigTyparNames.Length
                let nSigArgInfos = sigArgInfos.Length
                if ntps <> mtps then
                  err(fun(x, y, z) -> FSComp.SR.ValueNotContainedMutabilityGenericParametersDiffer(x, y, z, string mtps, string ntps))
                elif implValInfo.KindsOfTypars <> sigValInfo.KindsOfTypars then
                  err(FSComp.SR.ValueNotContainedMutabilityGenericParametersAreDifferentKinds)
                elif not (nSigArgInfos <= implArgInfos.Length && List.forall2 (fun x y -> List.length x <= List.length y) sigArgInfos (fst (List.splitAt nSigArgInfos implArgInfos))) then 
                  err(fun(x, y, z) -> FSComp.SR.ValueNotContainedMutabilityAritiesDiffer(x, y, z, id.idText, string nSigArgInfos, id.idText, id.idText))
                else 
                  let implArgInfos = implArgInfos |> List.truncate nSigArgInfos
                  let implArgInfos = (implArgInfos, sigArgInfos) ||> List.map2 (fun l1 l2 -> l1 |> List.take l2.Length)
                  // Propagate some information signature to implementation. 

                  // Check the attributes on each argument, and update the ValReprInfo for
                  // the value to reflect the information in the signature.
                  // This ensures that the compiled form of the value matches the signature rather than 
                  // the implementation. This also propagates argument names from signature to implementation
                  let res = 
                      (implArgInfos, sigArgInfos) ||> List.forall2 (List.forall2 (fun implArgInfo sigArgInfo -> 
                          checkAttribs aenv implArgInfo.Attribs sigArgInfo.Attribs (fun attribs -> 
                              match implArgInfo.Name, sigArgInfo.Name with 
                              | Some iname, Some sname when sname.idText <> iname.idText ->
                                   warning(ArgumentsInSigAndImplMismatch(sname, iname))
                              | _ -> ()
                              
                              let sigHasInlineIfLambda = HasFSharpAttribute g g.attrib_InlineIfLambdaAttribute sigArgInfo.Attribs
                              let implHasInlineIfLambda = HasFSharpAttribute g g.attrib_InlineIfLambdaAttribute implArgInfo.Attribs
                              let m = 
                                  match implArgInfo.Name with 
                                  | Some iname-> iname.idRange
                                  | None -> implVal.Range
                              if sigHasInlineIfLambda && not implHasInlineIfLambda then 
                                  errorR(Error (FSComp.SR.implMissingInlineIfLambda(), m))

                              implArgInfo.OtherRange <- sigArgInfo.Name |> Option.map (fun ident -> ident.idRange)
                              sigArgInfo.OtherRange <- implArgInfo.Name |> Option.map (fun ident -> ident.idRange)

                              implArgInfo.Name <- implArgInfo.Name |> Option.orElse sigArgInfo.Name
                              implArgInfo.Attribs <- attribs))) &&

                      checkAttribs aenv implRetInfo.Attribs sigRetInfo.Attribs (fun attribs -> 
                          implRetInfo.Name <- sigRetInfo.Name
                          implRetInfo.Attribs <- attribs)
                  
                  implVal.SetValReprInfo (Some (ValReprInfo (sigTyparNames, implArgInfos, implRetInfo)))
                  res

        and checkVal implModRef (aenv: TypeEquivEnv) (infoReader: InfoReader) (implVal: Val) (sigVal: Val) =

            // Propagate defn location information from implementation to signature . 
            sigVal.SetOtherRange (implVal.Range, true)
            implVal.SetOtherRange (sigVal.Range, false)
            implVal.SetOtherXmlDoc(sigVal.XmlDoc)

            let mk_err kind denv f = ValueNotContained(kind,denv, infoReader, implModRef, implVal, sigVal, f)
            let err denv f = errorR(mk_err RegularMismatch denv f); false
            let m = implVal.Range
            if implVal.IsMutable <> sigVal.IsMutable then (err denv FSComp.SR.ValueNotContainedMutabilityAttributesDiffer)
            elif implVal.LogicalName <> sigVal.LogicalName then (err denv FSComp.SR.ValueNotContainedMutabilityNamesDiffer)
            elif (implVal.CompiledName g.CompilerGlobalState) <> (sigVal.CompiledName g.CompilerGlobalState) then (err denv FSComp.SR.ValueNotContainedMutabilityCompiledNamesDiffer)
            elif implVal.DisplayName <> sigVal.DisplayName then (err denv FSComp.SR.ValueNotContainedMutabilityDisplayNamesDiffer)
            elif isLessAccessible implVal.Accessibility sigVal.Accessibility then (err denv FSComp.SR.ValueNotContainedMutabilityAccessibilityMore)
            elif implVal.ShouldInline <> sigVal.ShouldInline then (err denv FSComp.SR.ValueNotContainedMutabilityInlineFlagsDiffer)
            elif implVal.LiteralValue <> sigVal.LiteralValue then (err denv FSComp.SR.ValueNotContainedMutabilityLiteralConstantValuesDiffer)
            elif implVal.IsTypeFunction <> sigVal.IsTypeFunction then (err denv FSComp.SR.ValueNotContainedMutabilityOneIsTypeFunction)
            else 
                let implTypars, implValTy = implVal.GeneralizedType
                let sigTypars, sigValTy = sigVal.GeneralizedType
                if implTypars.Length <> sigTypars.Length then (err {denv with showTyparBinding=true} FSComp.SR.ValueNotContainedMutabilityParameterCountsDiffer) 
                else
                    let aenv = aenv.BindEquivTypars implTypars sigTypars 
                    checkTypars m aenv implTypars sigTypars &&
                        let strictTyEquals = typeAEquiv g aenv implValTy sigValTy
                        let onlyDiffersInNullness = not(strictTyEquals) && g.checkNullness && typeAEquiv g {aenv with NullnessMustEqual = false} implValTy sigValTy

                        // The types would be equal if we did not have nullness checks => lets just generate a warning, not an error
                        if onlyDiffersInNullness then
                            warning(mk_err NullnessOnlyMismatch denv FSComp.SR.ValueNotContainedMutabilityTypesDifferNullness)

                        if not strictTyEquals && not onlyDiffersInNullness then err denv FSComp.SR.ValueNotContainedMutabilityTypesDiffer                          
                        elif not (checkValInfo aenv (err denv) implVal sigVal) then false
                        elif implVal.IsExtensionMember <> sigVal.IsExtensionMember then err denv FSComp.SR.ValueNotContainedMutabilityExtensionsDiffer
                        elif not (checkMemberDatasConform (err denv) (implVal.Attribs, implVal, implVal.MemberInfo) (sigVal.Attribs, sigVal, sigVal.MemberInfo)) then false
                        else checkAttribs aenv implVal.Attribs sigVal.Attribs (fun attribs -> implVal.SetAttribs attribs)              


        and checkExnInfo err aenv (infoReader: InfoReader) (enclosingImplTycon: Tycon) (enclosingSigTycon: Tycon) implTypeRepr sigTypeRepr =
            match implTypeRepr, sigTypeRepr with
            | TExnAsmRepr _, TExnFresh _ ->
                (errorR (err FSComp.SR.ExceptionDefsNotCompatibleHiddenBySignature); false)
            | TExnAsmRepr tcr1, TExnAsmRepr tcr2  ->
                if tcr1 <> tcr2 then  (errorR (err FSComp.SR.ExceptionDefsNotCompatibleDotNetRepresentationsDiffer); false) else true
            | TExnAbbrevRepr _, TExnFresh _ ->
                (errorR (err FSComp.SR.ExceptionDefsNotCompatibleAbbreviationHiddenBySignature); false)
            | TExnAbbrevRepr ecr1, TExnAbbrevRepr ecr2 ->
                if not (tcrefAEquiv g aenv ecr1 ecr2) then
                  (errorR (err FSComp.SR.ExceptionDefsNotCompatibleSignaturesDiffer); false)
                else true
            | TExnFresh r1, TExnFresh  r2-> checkRecordFieldsForExn g denv err aenv infoReader enclosingImplTycon enclosingSigTycon r1 r2
            | TExnNone, TExnNone -> true
            | _ -> 
                (errorR (err FSComp.SR.ExceptionDefsNotCompatibleExceptionDeclarationsDiffer); false)

        and checkUnionCase aenv infoReader (enclosingImplTycon: Tycon) (enclosingSigTycon: Tycon) (implUnionCase: UnionCase) (sigUnionCase: UnionCase) =
            implUnionCase.SetOtherXmlDoc(sigUnionCase.XmlDoc)

            let err f = errorR(UnionCaseNotContained(denv, infoReader, enclosingImplTycon, implUnionCase, sigUnionCase, f));false
            sigUnionCase.OtherRangeOpt <- Some (implUnionCase.Range, true)
            implUnionCase.OtherRangeOpt <- Some (sigUnionCase.Range, false)
            if implUnionCase.Id.idText <> sigUnionCase.Id.idText then  err FSComp.SR.ModuleContainsConstructorButNamesDiffer
            elif implUnionCase.RecdFieldsArray.Length <> sigUnionCase.RecdFieldsArray.Length then err FSComp.SR.ModuleContainsConstructorButDataFieldsDiffer
            elif not (Array.forall2 (checkField aenv infoReader enclosingImplTycon enclosingSigTycon) implUnionCase.RecdFieldsArray sigUnionCase.RecdFieldsArray) then err FSComp.SR.ModuleContainsConstructorButTypesOfFieldsDiffer
            elif isLessAccessible implUnionCase.Accessibility sigUnionCase.Accessibility then err FSComp.SR.ModuleContainsConstructorButAccessibilityDiffers
            else checkAttribs aenv implUnionCase.Attribs sigUnionCase.Attribs (fun attribs -> implUnionCase.Attribs <- attribs)

        and checkField aenv infoReader (enclosingImplTycon: Tycon) (enclosingSigTycon: Tycon) implField sigField =
            implField.SetOtherXmlDoc(sigField.XmlDoc)

            let diag kind f = FieldNotContained(kind,denv, infoReader, enclosingImplTycon, enclosingSigTycon, implField, sigField, f)
            let err f = errorR(diag RegularMismatch f); false

            let areTypesDifferent() =
                let strictTyEquals = typeAEquiv g aenv implField.FormalType sigField.FormalType
                let onlyDiffersInNullness = not(strictTyEquals) && g.checkNullness &&  typeAEquiv g {aenv with NullnessMustEqual = false} implField.FormalType sigField.FormalType

                // The types would be equal if we did not have nullness checks => lets just generate a warning, not an error
                if onlyDiffersInNullness then
                    warning(diag NullnessOnlyMismatch FSComp.SR.FieldNotContainedTypesDifferNullness)
                    false
                else
                    not strictTyEquals  


            sigField.rfield_other_range <- Some (implField.Range, true)
            implField.rfield_other_range <- Some (sigField.Range, false)
            if implField.rfield_id.idText <> sigField.rfield_id.idText then err FSComp.SR.FieldNotContainedNamesDiffer
            elif isLessAccessible implField.Accessibility sigField.Accessibility then err FSComp.SR.FieldNotContainedAccessibilitiesDiffer
            elif implField.IsStatic <> sigField.IsStatic then err FSComp.SR.FieldNotContainedStaticsDiffer
            elif implField.IsMutable <> sigField.IsMutable then err FSComp.SR.FieldNotContainedMutablesDiffer
            elif implField.LiteralValue <> sigField.LiteralValue then err FSComp.SR.FieldNotContainedLiteralsDiffer
            elif areTypesDifferent() then err FSComp.SR.FieldNotContainedTypesDiffer
            else 
                checkAttribs aenv implField.FieldAttribs sigField.FieldAttribs (fun attribs -> implField.rfield_fattribs <- attribs) &&
                checkAttribs aenv implField.PropertyAttribs sigField.PropertyAttribs (fun attribs -> implField.rfield_pattribs <- attribs)
            
        and checkMemberDatasConform err  (_implAttrs, implVal, implMemberInfo) (_sigAttrs, sigVal, sigMemberInfo)  =
            match implMemberInfo, sigMemberInfo with 
            | None, None -> true
            | Some implMembInfo, Some sigMembInfo -> 
                if (implVal.CompiledName  g.CompilerGlobalState) <> (sigVal.CompiledName g.CompilerGlobalState) then 
                  err(FSComp.SR.ValueNotContainedMutabilityDotNetNamesDiffer)
                elif implMembInfo.MemberFlags.IsInstance <> sigMembInfo.MemberFlags.IsInstance then 
                  err(FSComp.SR.ValueNotContainedMutabilityStaticsDiffer)
                elif false then 
                  err(FSComp.SR.ValueNotContainedMutabilityVirtualsDiffer)
                elif not (implMembInfo.MemberFlags.IsDispatchSlot = sigMembInfo.MemberFlags.IsDispatchSlot) then 
                  err(FSComp.SR.ValueNotContainedMutabilityAbstractsDiffer)
               // The final check is an implication:
               //     classes have non-final CompareTo/Hash methods 
               //     abstract have non-final CompareTo/Hash methods 
               //     records  have final CompareTo/Hash methods 
               //     unions  have final CompareTo/Hash methods 
               // This is an example where it is OK for the signature to say 'non-final' when the implementation says 'final' 
                elif not implMembInfo.MemberFlags.IsFinal && sigMembInfo.MemberFlags.IsFinal then 
                  err(FSComp.SR.ValueNotContainedMutabilityFinalsDiffer)
                elif implMembInfo.MemberFlags.IsOverrideOrExplicitImpl <> sigMembInfo.MemberFlags.IsOverrideOrExplicitImpl then 
                  err(FSComp.SR.ValueNotContainedMutabilityOverridesDiffer)
                elif implMembInfo.MemberFlags.MemberKind <> sigMembInfo.MemberFlags.MemberKind then 
                  err(FSComp.SR.ValueNotContainedMutabilityOneIsConstructor)
                else  
                   let finstance = ValSpecIsCompiledAsInstance g sigVal
                   let ainstance = ValSpecIsCompiledAsInstance g implVal
                   if  finstance && not ainstance then 
                      err(FSComp.SR.ValueNotContainedMutabilityStaticButInstance)
                   elif not finstance && ainstance then 
                      err(FSComp.SR.ValueNotContainedMutabilityInstanceButStatic)
                   else true

            | _ -> false

        and checkRecordFields m aenv infoReader (implTycon: Tycon) (sigTycon: Tycon) (implFields: TyconRecdFields) (sigFields: TyconRecdFields) =
            let implFields = implFields.TrueFieldsAsList
            let sigFields = sigFields.TrueFieldsAsList
            let m1 = implFields |> NameMap.ofKeyedList (fun rfld -> rfld.LogicalName)
            let m2 = sigFields |> NameMap.ofKeyedList (fun rfld -> rfld.LogicalName)
            NameMap.suball2 
                (fun fieldName _ -> errorR(Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleFieldRequiredButNotSpecified(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName, fieldName), m)); false) 
                (checkField aenv infoReader implTycon sigTycon) m1 m2 &&
            NameMap.suball2 
                (fun fieldName _ -> errorR(Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleFieldWasPresent(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName, fieldName), m)); false) 
                (fun x y -> checkField aenv infoReader implTycon sigTycon y x) m2 m1 &&

            // This check is required because constructors etc. are externally visible 
            // and thus compiled representations do pick up dependencies on the field order  
            (if List.forall2 (checkField aenv infoReader implTycon sigTycon)  implFields sigFields
             then true
             else (errorR(Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleFieldOrderDiffer(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)); false))

        and checkRecordFieldsForExn _g _denv err aenv (infoReader: InfoReader) (enclosingImplTycon: Tycon) (enclosingSigTycon: Tycon) (implFields: TyconRecdFields) (sigFields: TyconRecdFields) =
            let implFields = implFields.TrueFieldsAsList
            let sigFields = sigFields.TrueFieldsAsList
            let m1 = implFields |> NameMap.ofKeyedList (fun rfld -> rfld.LogicalName)
            let m2 = sigFields |> NameMap.ofKeyedList (fun rfld -> rfld.LogicalName)
            NameMap.suball2 (fun s _ -> errorR(err (fun (x, y) -> FSComp.SR.ExceptionDefsNotCompatibleFieldInSigButNotImpl(s, x, y))); false) (checkField aenv infoReader enclosingImplTycon enclosingSigTycon)  m1 m2 &&
            NameMap.suball2 (fun s _ -> errorR(err (fun (x, y) -> FSComp.SR.ExceptionDefsNotCompatibleFieldInImplButNotSig(s, x, y))); false) (fun x y -> checkField aenv infoReader enclosingImplTycon enclosingSigTycon y x)  m2 m1 &&
            // This check is required because constructors etc. are externally visible
            // and thus compiled representations do pick up dependencies on the field order
            (if List.forall2 (checkField aenv infoReader enclosingImplTycon enclosingSigTycon)  implFields sigFields
             then true
             else (errorR(err FSComp.SR.ExceptionDefsNotCompatibleFieldOrderDiffers); false))

        and checkVirtualSlots denv infoReader m (implTycon: Tycon) implAbstractSlots sigAbstractSlots =
            let m1 = NameMap.ofKeyedList (fun (v: ValRef) -> v.DisplayName) implAbstractSlots
            let m2 = NameMap.ofKeyedList (fun (v: ValRef) -> v.DisplayName) sigAbstractSlots
            (m1, m2) ||> NameMap.suball2 (fun _s vref -> 
                let kindText = implTycon.TypeOrMeasureKind.ToString()
                let valText = NicePrint.stringValOrMember denv infoReader vref
                errorR(Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleAbstractMemberMissingInImpl(kindText, implTycon.DisplayName, valText), m)); false) (fun _x _y -> true)  &&

            (m2, m1) ||> NameMap.suball2 (fun _s vref -> 
                let kindText = implTycon.TypeOrMeasureKind.ToString()
                let valText = NicePrint.stringValOrMember denv infoReader vref
                errorR(Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleAbstractMemberMissingInSig(kindText, implTycon.DisplayName, valText), m)); false) (fun _x _y -> true)  

        and checkClassFields isStruct m aenv infoReader (implTycon: Tycon) (signTycon: Tycon) (implFields: TyconRecdFields) (sigFields: TyconRecdFields) =
            let implFields = implFields.TrueFieldsAsList
            let sigFields = sigFields.TrueFieldsAsList
            let m1 = implFields |> NameMap.ofKeyedList (fun rfld -> rfld.LogicalName) 
            let m2 = sigFields |> NameMap.ofKeyedList (fun rfld -> rfld.LogicalName) 
            NameMap.suball2 
                (fun fieldName _ -> errorR(Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleFieldRequiredButNotSpecified(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName, fieldName), m)); false) 
                (checkField aenv infoReader implTycon signTycon) m1 m2 &&
            (if isStruct then 
                NameMap.suball2 
                    (fun fieldName _ -> warning(Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleFieldIsInImplButNotSig(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName, fieldName), m)); true) 
                    (fun x y -> checkField aenv infoReader implTycon signTycon y x) m2 m1
             else
                true)
            

        and checkTypeRepr m aenv (infoReader: InfoReader) (implTycon: Tycon) (sigTycon: Tycon) =
            let reportNiceError k s1 s2 = 
              let aset = NameSet.ofList s1
              let fset = NameSet.ofList s2
              match Zset.elements (Zset.diff aset fset) with 
              | [] -> 
                  match Zset.elements (Zset.diff fset aset) with             
                  | [] -> (errorR (Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleNumbersDiffer(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName, k), m)); false)
                  | l -> (errorR (Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleSignatureDefinesButImplDoesNot(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName, k, String.concat ";" l), m)); false)
              | l -> (errorR (Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleImplDefinesButSignatureDoesNot(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName, k, String.concat ";" l), m)); false)

            match implTycon.TypeReprInfo, sigTycon.TypeReprInfo with 
            | (TILObjectRepr _ 
#if !NO_TYPEPROVIDERS
              | TProvidedTypeRepr _ 
              | TProvidedNamespaceRepr _
#endif
              ), TNoRepr  -> true
            | TFSharpTyconRepr r, TNoRepr  -> 
                match r.fsobjmodel_kind with 
                | TFSharpStruct | TFSharpEnum -> 
                   (errorR (Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleImplDefinesStruct(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)); false)
                | _ -> 
                   true
            | TAsmRepr _, TNoRepr -> 
                (errorR (Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleDotNetTypeRepresentationIsHidden(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)); false)
            | TMeasureableRepr _, TNoRepr -> 
                (errorR (Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleTypeIsHidden(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)); false)

            // Union types are compatible with union types in signature
            | TFSharpTyconRepr { fsobjmodel_kind=TFSharpUnion; fsobjmodel_cases=r1},
              TFSharpTyconRepr { fsobjmodel_kind=TFSharpUnion; fsobjmodel_cases=r2} -> 
                let ucases1 = r1.UnionCasesAsList
                let ucases2 = r2.UnionCasesAsList
                if ucases1.Length <> ucases2.Length then
                  let names (l: UnionCase list) = l |> List.map (fun c -> c.Id.idText)
                  reportNiceError "union case" (names ucases1) (names ucases2) 
                else List.forall2 (checkUnionCase aenv infoReader implTycon sigTycon) ucases1 ucases2

            // Record types are compatible with union types in signature
            | TFSharpTyconRepr { fsobjmodel_kind=TFSharpRecord; fsobjmodel_rfields=implFields},
              TFSharpTyconRepr { fsobjmodel_kind=TFSharpRecord; fsobjmodel_rfields=sigFields} -> 
                checkRecordFields m aenv infoReader implTycon sigTycon implFields sigFields

            // Record types are compatible with union types in signature
            | TFSharpTyconRepr r1, TFSharpTyconRepr r2 -> 
                let compat =
                    match r1.fsobjmodel_kind, r2.fsobjmodel_kind with 
                    | TFSharpRecord, TFSharpClass -> true
                    | TFSharpClass, TFSharpClass -> true
                    | TFSharpInterface, TFSharpInterface -> true
                    | TFSharpStruct, TFSharpStruct -> true
                    | TFSharpEnum, TFSharpEnum -> true
                    | TFSharpDelegate (TSlotSig(_, typ1, ctps1, mtps1, ps1, rty1)),
                      TFSharpDelegate (TSlotSig(_, typ2, ctps2, mtps2, ps2, rty2)) -> 
                             (typeAEquiv g aenv typ1 typ2) &&
                             (ctps1.Length = ctps2.Length) &&
                             (let aenv = aenv.BindEquivTypars ctps1 ctps2 
                              (typarsAEquiv g aenv ctps1 ctps2) &&
                              (mtps1.Length = mtps2.Length) &&
                              (let aenv = aenv.BindEquivTypars mtps1 mtps2 
                               (typarsAEquiv g aenv mtps1 mtps2) &&
                               ((ps1, ps2) ||> List.lengthsEqAndForall2 (List.lengthsEqAndForall2 (fun p1 p2 -> typeAEquiv g aenv p1.Type p2.Type))) &&
                               (returnTypesAEquiv g aenv rty1 rty2)))
                    | _ -> false
                if not compat then
                    errorR (Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleTypeIsDifferentKind(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m))
                    false
                else 
                  let isStruct = (match r1.fsobjmodel_kind with TFSharpStruct -> true | _ -> false)
                  checkClassFields isStruct m aenv infoReader implTycon sigTycon r1.fsobjmodel_rfields r2.fsobjmodel_rfields &&
                  checkVirtualSlots denv infoReader m implTycon r1.fsobjmodel_vslots r2.fsobjmodel_vslots
            | TAsmRepr tcr1,  TAsmRepr tcr2 -> 
                if tcr1 <> tcr2 then  (errorR (Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleILDiffer(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)); false) else true
            | TMeasureableRepr ty1,  TMeasureableRepr ty2 -> 
                if typeAEquiv g aenv ty1 ty2 then true else (errorR (Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleRepresentationsDiffer(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)); false)
            | TNoRepr, TNoRepr -> true
#if !NO_TYPEPROVIDERS
            | TProvidedTypeRepr info1, TProvidedTypeRepr info2 ->  
                Tainted.EqTainted info1.ProvidedType.TypeProvider info2.ProvidedType.TypeProvider && ProvidedType.TaintedEquals(info1.ProvidedType, info2.ProvidedType)
            | TProvidedNamespaceRepr _, TProvidedNamespaceRepr _ -> 
                System.Diagnostics.Debug.Assert(false, "unreachable: TProvidedNamespaceRepr only on namespaces, not types" )
                true
#endif
            | TNoRepr, _ -> (errorR (Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleRepresentationsDiffer(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)); false)
            | _, _ -> (errorR (Error(FSComp.SR.DefinitionsInSigAndImplNotCompatibleRepresentationsDiffer(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)); false)

        and checkTypeAbbrev m aenv (implTycon: Tycon) (sigTycon: Tycon) =
            let kind1 = implTycon.TypeOrMeasureKind
            let kind2 = sigTycon.TypeOrMeasureKind
            if kind1 <> kind2 then (errorR (Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleSignatureDeclaresDiffer(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName, kind2.ToString(), kind1.ToString()), m)); false)
            else
              match implTycon.TypeAbbrev, sigTycon.TypeAbbrev with 
              | Some ty1, Some ty2 -> 
                  if not (typeAEquiv g aenv ty1 ty2) then 
                      errorR (DefinitionsInSigAndImplNotCompatibleAbbreviationsDiffer(denv, implTycon, sigTycon, ty1, ty2, m))
                      false 
                  else 
                      true
              | None, None -> true
              | Some _, None -> (errorR (Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleAbbreviationHiddenBySig(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)); false)
              | None, Some _ -> (errorR (Error (FSComp.SR.DefinitionsInSigAndImplNotCompatibleSigHasAbbreviation(implTycon.TypeOrMeasureKind.ToString(), implTycon.DisplayName), m)); false)

        and checkModuleOrNamespaceContents m aenv (infoReader: InfoReader) (implModRef: ModuleOrNamespaceRef) (signModType: ModuleOrNamespaceType) = 
            let implModType = implModRef.ModuleOrNamespaceType
            (if implModType.ModuleOrNamespaceKind <> signModType.ModuleOrNamespaceKind then errorR(Error(FSComp.SR.typrelModuleNamespaceAttributesDifferInSigAndImpl(), m)))


            (implModType.TypesByMangledName, signModType.TypesByMangledName)
             ||> NameMap.suball2 
                (fun s _fx -> errorR(RequiredButNotSpecified(denv, implModRef, "type", (fun os -> Printf.bprintf os "%s" s), m)); false) 
                (checkTypeDef aenv infoReader)  &&


            (implModType.ModulesAndNamespacesByDemangledName, signModType.ModulesAndNamespacesByDemangledName ) 
              ||> NameMap.suball2 
                   (fun s fx -> errorR(RequiredButNotSpecified(denv, implModRef, (if fx.IsModule then "module" else "namespace"), (fun os -> Printf.bprintf os "%s" s), m)); false) 
                   (fun x1 x2 -> checkModuleOrNamespace aenv infoReader (mkLocalModuleRef x1) x2)  &&

            let sigValHadNoMatchingImplementation (fx: Val) (_closeActualVal: Val option) = 
                errorR(RequiredButNotSpecified(denv, implModRef, "value", (fun os -> 
                   (* In the case of missing members show the full required enclosing type and signature *)
                   if fx.IsMember then 
                       NicePrint.outputQualifiedValOrMember denv infoReader os (mkLocalValRef fx)
                   else
                       Printf.bprintf os "%s" fx.DisplayName), m))
            
            let valuesPartiallyMatch (av: Val) (fv: Val) =
                let akey = av.GetLinkagePartialKey()
                let fkey = fv.GetLinkagePartialKey()
                (akey.MemberParentMangledName = fkey.MemberParentMangledName) &&
                (akey.LogicalName = fkey.LogicalName) &&
                (akey.TotalArgCount = fkey.TotalArgCount)    
                                       
            (implModType.AllValsAndMembersByLogicalNameUncached, signModType.AllValsAndMembersByLogicalNameUncached)
              ||> NameMap.suball2 
                    (fun _s (fxs: Val list) -> sigValHadNoMatchingImplementation fxs.Head None; false)
                    (fun avs fvs -> 
                        match avs, fvs with 
                        | [], _ | _, [] -> failwith "unreachable"
                        | [av], [fv] -> 
                            if valuesPartiallyMatch av fv then
                                checkVal implModRef aenv infoReader av fv
                            else
                                sigValHadNoMatchingImplementation fv None
                                false    
                        | _ -> 
                             // for each formal requirement, try to find a precisely matching actual requirement
                             let matchingPairs = 
                                 fvs |> List.choose (fun fv -> 
                                     match avs |> List.tryFind (fun av -> valLinkageAEquiv g aenv av fv) with
                                      | None -> None
                                      | Some av -> Some(fv, av))
                             
                             // Check the ones with matching linkage
                             let allPairsOk = matchingPairs |> List.map (fun (fv, av) -> checkVal implModRef aenv infoReader av fv) |> List.forall id
                             let someNotOk = matchingPairs.Length < fvs.Length
                             // Report an error for those that don't. Try pairing up by enclosing-type/name
                             if someNotOk then 
                                 let noMatches, partialMatchingPairs = 
                                     fvs |> List.splitChoose (fun fv -> 
                                         match avs |> List.tryFind (fun av -> valuesPartiallyMatch av fv) with 
                                          | None -> Choice1Of2 fv
                                          | Some av -> Choice2Of2(fv, av))
                                 for fv, av in partialMatchingPairs do
                                     checkVal implModRef aenv infoReader av fv |> ignore
                                 for fv in noMatches do 
                                     sigValHadNoMatchingImplementation fv None
                             allPairsOk && not someNotOk)


        and checkModuleOrNamespace aenv (infoReader: InfoReader) implModRef sigModRef =
            implModRef.SetOtherXmlDoc(sigModRef.XmlDoc)
            // Propagate defn location information from implementation to signature . 
            sigModRef.SetOtherRange (implModRef.Range, true)
            implModRef.Deref.SetOtherRange (sigModRef.Range, false)
            checkModuleOrNamespaceContents implModRef.Range aenv infoReader implModRef sigModRef.ModuleOrNamespaceType &&
            checkAttribs aenv implModRef.Attribs sigModRef.Attribs implModRef.Deref.SetAttribs

        member _.CheckSignature aenv (infoReader: InfoReader) (implModRef: ModuleOrNamespaceRef) (signModType: ModuleOrNamespaceType) = 
            checkModuleOrNamespaceContents implModRef.Range aenv infoReader implModRef signModType

        member _.CheckTypars m aenv (implTypars: Typars) (signTypars: Typars) = 
            checkTypars m aenv implTypars signTypars


/// Check the names add up between a signature and its implementation. We check this first.
let rec CheckNamesOfModuleOrNamespaceContents denv infoReader (implModRef: ModuleOrNamespaceRef) (signModType: ModuleOrNamespaceType) = 
        let m = implModRef.Range 
        let implModType = implModRef.ModuleOrNamespaceType
        NameMap.suball2 
            (fun s _fx -> errorR(RequiredButNotSpecified(denv, implModRef, "type", (fun os -> Printf.bprintf os "%s" s), m)); false) 
            (fun _ _ -> true)  
            implModType.TypesByMangledName 
            signModType.TypesByMangledName &&

        (implModType.ModulesAndNamespacesByDemangledName, signModType.ModulesAndNamespacesByDemangledName ) 
          ||> NameMap.suball2 
                (fun s fx -> errorR(RequiredButNotSpecified(denv, implModRef, (if fx.IsModule then "module" else "namespace"), (fun os -> Printf.bprintf os "%s" s), m)); false) 
                (fun x1 (x2: ModuleOrNamespace) -> CheckNamesOfModuleOrNamespace denv infoReader (mkLocalModuleRef x1) x2.ModuleOrNamespaceType)  &&

        (implModType.AllValsAndMembersByLogicalNameUncached, signModType.AllValsAndMembersByLogicalNameUncached) 
          ||> NameMap.suball2 
                (fun _s (fxs: Val list) -> 
                    let fx = fxs.Head
                    errorR(RequiredButNotSpecified(denv, implModRef, "value", (fun os -> 
                       // In the case of missing members show the full required enclosing type and signature 
                       if Option.isSome fx.MemberInfo then 
                           NicePrint.outputQualifiedValOrMember denv infoReader os (mkLocalValRef fx)
                       else
                           Printf.bprintf os "%s" fx.DisplayName), m)); false)
                (fun _ _ -> true) 


and CheckNamesOfModuleOrNamespace denv (infoReader: InfoReader) (implModRef: ModuleOrNamespaceRef) signModType = 
        CheckNamesOfModuleOrNamespaceContents denv infoReader implModRef signModType

